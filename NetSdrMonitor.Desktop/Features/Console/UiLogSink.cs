using System.Collections.Concurrent;

namespace NetSdrMonitor.Desktop.Features.Console;

/// <summary>
/// Потокобезпечний буфер логів між джерелом (логер пише з фонових потоків) і UI-консоллю
/// (споживач забирає батчами на UI-потоці). Сам нічого не знає про WPF — лише черга.
/// </summary>
public sealed class UiLogSink
{
   private readonly ConcurrentQueue<LogEntry> _queue = new();

   /// <summary>
   /// Додає запис у чергу (викликається з будь-якого потоку логером).
   /// </summary>
   public void Write(LogEntry entry) => _queue.Enqueue(entry);

   /// <summary>
   /// Забирає наступний запис; повертає false, коли черга порожня.
   /// </summary>
   public bool TryDequeue(out LogEntry entry) => _queue.TryDequeue(out entry!);
}
