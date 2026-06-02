using System.Buffers.Binary;
using Microsoft.Extensions.Logging.Abstractions;
using NetSdrMonitor.Communication.Monitor;
using NetSdrMonitor.Communication.Tcp;
using NetSdrMonitor.Core.Abstractions.Communication;
using NetSdrMonitor.Domain.Signals;
using NetSdrMonitor.Protocol;
using NetSdrMonitor.Protocol.ControlItems;
using NetSdrMonitor.Protocol.Messages;

namespace NetSdrMonitor.IntegrationTests.Communication;

/// <summary>
/// Наскрізні тести монітора поверх справжнього TCP (loopback): монітор ← TcpClientTransport →
/// керований таргет. Перевіряють весь шлях «байти → framing → парсер → Signal»: коректні сигнали,
/// толерантність до битих/чужих кадрів, склейку кадру на стику сегментів, автопідключення після
/// обриву та ввічливу зупинку. Кожен тест тримає власний таргет на окремому порту — ізольовано.
/// </summary>
public sealed class SdrMonitorIntegrationTests
{
    // спільний дедлайн на очікування сигналу/відповіді: достатній для loopback, але не «вічний» на збої
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(5);

    private readonly ISdrProtocol _protocol = new SdrProtocol();
    private readonly SdrMessageParser _parser = new();

    [Fact]
    public async Task Connecting_SendsRunCommandToTarget()
    {
        await using var target = new ScriptedSignalTarget();
        await using SdrMonitor monitor = BuildMonitor(target.Port);
        using var cts = new CancellationTokenSource(ResponseTimeout);

        monitor.Start();
        await using TargetConnection connection = await target.AcceptAsync(cts.Token);

        // перше, що монітор шле новому з'єднанню, — команда Run (інакше таргет не почне стрімити)
        SdrMessage first = await connection.ReadMessageAsync(cts.Token);

        Assert.True(_protocol.TryGetReceiverState(first, out ReceiverState state));
        Assert.Equal(ReceiverState.Running, state);
    }

    [Fact]
    public async Task ValidSignals_ArriveDecodedInOrder()
    {
        await using var target = new ScriptedSignalTarget();
        await using SdrMonitor monitor = BuildMonitor(target.Port);
        using var cts = new CancellationTokenSource(ResponseTimeout);

        monitor.Start();
        await using TargetConnection connection = await target.AcceptAsync(cts.Token);
        await connection.WaitForReceiverStateAsync(ReceiverState.Running, cts.Token);

        Signal[] sent =
        [
                    SignalAt(14_010_000, 7.5),
                    SignalAt(14_012_000, 12.0),
                    SignalAt(14_011_000, 21.3),
        ];

        // починаємо читати ДО відправлення: канал монітора все одно буферизує, але так нема гонки
        Task<List<Signal>> collecting = CollectAsync(monitor, sent.Length, cts.Token);
        foreach (Signal signal in sent)
            await connection.SendSignalAsync(signal, cts.Token);

        List<Signal> received = await collecting;

        Assert.Equal(sent, received); // record => рівність за всіма полями; порядок збережено
        Assert.Equal(ConnectionStatus.Connected, monitor.Status);
    }

    [Fact]
    public async Task MalformedDataItem_IsSkipped_AndStreamStaysInSync()
    {
        await using var target = new ScriptedSignalTarget();
        await using SdrMonitor monitor = BuildMonitor(target.Port);
        using var cts = new CancellationTokenSource(ResponseTimeout);

        monitor.Start();
        await using TargetConnection connection = await target.AcceptAsync(cts.Token);
        await connection.WaitForReceiverStateAsync(ReceiverState.Running, cts.Token);

        Signal before = SignalAt(7_050_000);
        Signal after = SignalAt(7_051_000);

        Task<List<Signal>> collecting = CollectAsync(monitor, 2, cts.Token);
        await connection.SendSignalAsync(before, cts.Token);
        await connection.SendRawAsync(MalformedDataItemFrame(), cts.Token); // має бути проковтнутий
        await connection.SendSignalAsync(after, cts.Token);

        List<Signal> received = await collecting;

        // битий кадр не дав сигналу й не зрушив межі — наступний валідний кадр прочитано правильно
        Signal[] expected = [before, after];
        Assert.Equal(expected, received);
    }

    [Fact]
    public async Task UnsupportedControlItem_IsAnsweredWithNak()
    {
        await using var target = new ScriptedSignalTarget();
        await using SdrMonitor monitor = BuildMonitor(target.Port);
        using var cts = new CancellationTokenSource(ResponseTimeout);

        monitor.Start();
        await using TargetConnection connection = await target.AcceptAsync(cts.Token);
        await connection.WaitForReceiverStateAsync(ReceiverState.Running, cts.Token); // з'їли Run

        await connection.SendRawAsync(UnsupportedControlFrame(), cts.Token);
        SdrMessage reply = await connection.ReadMessageAsync(cts.Token);

        // NAK — порожній Set Control Item (заголовок [02][00]) без коду control item
        Assert.Equal(SdrMessageType.SetControlItem, reply.Header.Type);
        Assert.Equal(SdrMessageHeader.Size, reply.Header.Length);
        Assert.Null(reply.ControlCode);

        // і потік не зламано: наступний валідний сигнал доходить як раніше
        Signal next = SignalAt(21_000_000);
        Task<List<Signal>> collecting = CollectAsync(monitor, 1, cts.Token);
        await connection.SendSignalAsync(next, cts.Token);
        Assert.Equal(next, Assert.Single(await collecting));
    }

    [Fact]
    public async Task FrameSplitAcrossSegments_IsReassembled()
    {
        await using var target = new ScriptedSignalTarget();
        await using SdrMonitor monitor = BuildMonitor(target.Port);
        using var cts = new CancellationTokenSource(ResponseTimeout);

        monitor.Start();
        await using TargetConnection connection = await target.AcceptAsync(cts.Token);
        await connection.WaitForReceiverStateAsync(ReceiverState.Running, cts.Token);

        Signal signal = SignalAt(28_500_000, 33.0);
        byte[] frame = _parser.FromSignal(signal).Raw.ToArray();

        Task<List<Signal>> collecting = CollectAsync(monitor, 1, cts.Token);
        // ріжемо навіть заголовок: 1 байт, пауза, тоді решта — монітор має зібрати кадр із кількох читань
        await connection.SendRawAsync(frame.AsMemory(0, 1), cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(30), cts.Token);
        await connection.SendRawAsync(frame.AsMemory(1), cts.Token);

        Assert.Equal(signal, Assert.Single(await collecting));
    }

    [Fact]
    public async Task PeerDrop_TriggersReconnect_AndStreamResumes()
    {
        await using var target = new ScriptedSignalTarget();
        await using SdrMonitor monitor = BuildMonitor(target.Port);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        monitor.Start();

        // перше з'єднання: приймаємо один сигнал, тоді таргет рве зв'язок
        TargetConnection first = await target.AcceptAsync(cts.Token);
        await first.WaitForReceiverStateAsync(ReceiverState.Running, cts.Token);

        Signal beforeDrop = SignalAt(10_000_000);
        Task<List<Signal>> beforeBatch = CollectAsync(monitor, 1, cts.Token);
        await first.SendSignalAsync(beforeDrop, cts.Token);
        Assert.Equal(beforeDrop, Assert.Single(await beforeBatch));
        await first.DisposeAsync(); // обрив із боку таргета

        // монітор має сам перепідключитись і знову попросити Run
        await using TargetConnection second = await target.AcceptAsync(cts.Token);
        await second.WaitForReceiverStateAsync(ReceiverState.Running, cts.Token);

        Signal afterReconnect = SignalAt(10_001_000);
        Task<List<Signal>> afterBatch = CollectAsync(monitor, 1, cts.Token);
        await second.SendSignalAsync(afterReconnect, cts.Token);

        Assert.Equal(afterReconnect, Assert.Single(await afterBatch));
    }

    [Fact]
    public async Task StopAsync_SendsStopCommand_AndEndsInStoppedStatus()
    {
        await using var target = new ScriptedSignalTarget();
        await using SdrMonitor monitor = BuildMonitor(target.Port);
        using var cts = new CancellationTokenSource(ResponseTimeout);

        monitor.Start();
        await using TargetConnection connection = await target.AcceptAsync(cts.Token);
        await connection.WaitForReceiverStateAsync(ReceiverState.Running, cts.Token);

        // слухаємо Stop ще до зупинки — щоб упіймати його незалежно від миті закриття сокета
        Task observeStop = connection.WaitForReceiverStateAsync(ReceiverState.Stopped, cts.Token);
        await monitor.StopAsync();
        await observeStop; // монітор ввічливо попросив зупинити приймач перед розривом

        Assert.Equal(ConnectionStatus.Stopped, monitor.Status);
    }

    [Fact]
    public async Task StatusChanged_RaisesConnectingThenConnected()
    {
        await using var target = new ScriptedSignalTarget();
        await using SdrMonitor monitor = BuildMonitor(target.Port);
        using var cts = new CancellationTokenSource(ResponseTimeout);

        var statuses = new List<ConnectionStatus>();
        monitor.StatusChanged += (_, status) =>
        {
            lock (statuses) // подія здіймається з фонової петлі — захищаємо список
                statuses.Add(status);
        };

        monitor.Start();
        await using TargetConnection connection = await target.AcceptAsync(cts.Token);
        await connection.WaitForReceiverStateAsync(ReceiverState.Running, cts.Token);

        // дочекаємось першого сигналу — гарантія, що петля вже пройшла перехід у Connected
        Task<List<Signal>> collecting = CollectAsync(monitor, 1, cts.Token);
        await connection.SendSignalAsync(SignalAt(15_000_000), cts.Token);
        await collecting;

        ConnectionStatus[] snapshot;
        lock (statuses)
            snapshot = [.. statuses];

        Assert.Equal(ConnectionStatus.Connecting, snapshot[0]); // перший перехід — саме спроба підключення
        Assert.Contains(ConnectionStatus.Connected, snapshot);
    }

    private static SdrMonitor BuildMonitor(int port) => new(
                NullLogger<SdrMonitor>.Instance,
                new TcpClientTransportFactory("127.0.0.1", port),
                FastOptions());

    // швидкий реконект і скромні таймаути: тести не повинні залежати від «бойових» 2-10 с
    private static SdrMonitorOptions FastOptions() => new()
    {
                ConnectTimeout = TimeSpan.FromSeconds(2),
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                IdleTimeout    = TimeSpan.FromSeconds(30), // на idle-таймаут ці тести свідомо не покладаються
    };

    // Читає з каналу монітора рівно count сигналів або повертає зібране при таймауті (тоді assert дасть зрозумілий збій).
    private static async Task<List<Signal>> CollectAsync(ISdrMonitor monitor, int count, CancellationToken ct)
    {
        var received = new List<Signal>(count);
        try
        {
            await foreach (Signal signal in monitor.Signals(ct))
            {
                received.Add(signal);
                if (received.Count >= count)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // дедлайн вичерпано — віддаємо часткове, порівняння кількості/значень покаже причину
        }

        return received;
    }

    // Детермінований сигнал у смузі 10 кГц — значення задаємо самі, тож round-trip має збігтися побайтно.
    private static Signal SignalAt(ulong frequencyHz, double snrDb = 12.5) => new()
    {
                TimestampUnixMs = 1_700_000_000_000,
                FrequencyHz     = frequencyHz,
                BandwidthHz     = 10_000,
                SnrDb           = snrDb,
    };

    // Валідно обрамлений Data Item 0 з тілом ≠ 28 байт: framing прийме кадр, парсер відхилить як «не сигнал».
    private static byte[] MalformedDataItemFrame()
    {
        const int wrongPayload = 8; // навмисно не 28
        SdrMessageHeader header = SdrMessageHeader.FromPayload(SdrMessageType.DataItem0, wrongPayload);

        var raw = new byte[header.Length];
        header.WriteTo(raw);
        return raw; // тіло лишаємо нулями — парсеру важливий лише розмір
    }

    // Set Control Item з кодом, який монітор не підтримує (RF Gain) => очікуємо у відповідь NAK.
    private static byte[] UnsupportedControlFrame()
    {
        const int controlCodeSize = 2;
        SdrMessageHeader header = SdrMessageHeader.FromPayload(SdrMessageType.SetControlItem, controlCodeSize);

        var raw = new byte[header.Length];
        header.WriteTo(raw);
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(SdrMessageHeader.Size), (ushort)ControlItemCode.RfGain);
        return raw;
    }
}
