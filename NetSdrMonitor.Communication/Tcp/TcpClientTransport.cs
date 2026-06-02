using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using NetSdrMonitor.Core.Abstractions.Communication;

namespace NetSdrMonitor.Communication.Tcp;

/// <summary>
/// Транспорт-клієнт поверх <see cref="TcpClient"/>: дозвонюється до таргета й віддає його дуплекс
/// як байтовий потік (<see cref="PipeReader"/> на прийомі, серіалізований запис на відправленні).
/// Одноразовий на одне з'єднання - переустановлення робить <see cref="RestartAsync"/> (новий сокет).
/// </summary>
public sealed class TcpClientTransport(string host, int port) : ITransport
{
   private const int MinUserPort = 1024; // 0..1023 — системні/well-known, не займаємо
   private const int MaxUserPort = 65535;

   private readonly string _host = NormalizeHost(host);
   private readonly int _port = ValidatePort(port);
   private readonly SemaphoreSlim _sendGate = new(1, 1); // Run/Stop і ACK летять з різних задач

   private TcpClient? _client;
   private NetworkStream? _stream;
   private PipeReader? _reader;
   private bool _completed; // пір закрив потік: наступний Receive віддасть порожньо

   /// <inheritdoc />
   public bool IsConnected => _client?.Connected ?? false;

   /// <inheritdoc />
   public Task ConnectAsync(CancellationToken ct = default) => EstablishAsync(ct);

   /// <inheritdoc />
   public async Task RestartAsync(CancellationToken ct = default)
   {
      await TearDownAsync(); // скидаємо старий сокет (одноразовий — повторно не під'єднується)
      await EstablishAsync(ct);
   }

   /// <inheritdoc />
   public async Task<ReadOnlySequence<byte>> ReceiveAsync(CancellationToken ct = default)
   {
      if (_reader is null)
         throw new InvalidOperationException("ReceiveAsync викликано до підключення.");

      if (_completed)
         return ReadOnlySequence<byte>.Empty;

      ReadResult result = await _reader.ReadAsync(ct);
      _completed = result.IsCompleted;
      return result.Buffer;
   }

   /// <inheritdoc />
   public void AdvanceTo(SequencePosition consumed, SequencePosition examined) => _reader!.AdvanceTo(consumed, examined);

   /// <inheritdoc />
   public async Task SendAsync(ReadOnlyMemory<byte> bytes, CancellationToken ct = default)
   {
      if (_stream is null)
         throw new InvalidOperationException("SendAsync викликано до підключення.");

      await _sendGate.WaitAsync(ct);
      try
      {
         await _stream.WriteAsync(bytes, ct);
      }
      finally
      {
         _sendGate.Release();
      }
   }

   /// <inheritdoc />
   public async ValueTask DisposeAsync()
   {
      await TearDownAsync();
      _sendGate.Dispose();
   }

   private async Task EstablishAsync(CancellationToken ct)
   {
      var client = new TcpClient
      {
            NoDelay = true
      }; // NoDelay: дрібні кадри без затримки Нейгла
      try
      {
         await client.ConnectAsync(_host, _port, ct); // одна спроба; кине SocketException при невдачі
      }
      catch
      {
         client.Dispose(); // невдалу спробу прибираємо одразу — інакше при ретраях течуть напіввідкриті сокети
         throw;
      }

      _client    = client;
      _stream    = client.GetStream();
      _reader    = PipeReader.Create(_stream);
      _completed = false;
   }

   private async Task TearDownAsync()
   {
      if (_reader is not null)
      {
         await _reader.CompleteAsync();
         _reader = null;
      }
      if (_stream is not null)
      {
         await _stream.DisposeAsync();
         _stream = null;
      }
      _client?.Dispose();
      _client    = null;
      _completed = false;
   }

   private static string NormalizeHost(string host)
   {
      if (string.IsNullOrWhiteSpace(host))
         throw new ArgumentException("Host не може бути порожнім.", nameof(host));

      string trimmed = host.Trim().TrimEnd('/');

      if (Uri.CheckHostName(trimmed) == UriHostNameType.Unknown)
         throw new ArgumentException($"Невалідний host: '{host}'.", nameof(host));

      return trimmed;
   }

   private static int ValidatePort(int port)
   {
      if (port is < MinUserPort or > MaxUserPort)
         throw new ArgumentOutOfRangeException(
               nameof(port), port, $"Порт має бути в діапазоні {MinUserPort}..{MaxUserPort} (без системних 0..1023).");

      return port;
   }
}
