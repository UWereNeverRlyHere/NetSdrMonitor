using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using NetSdrMonitor.Communication.Monitor;
using NetSdrMonitor.Communication.Server;
using NetSdrMonitor.Core.Abstractions.Communication;
using NetSdrMonitor.Domain.Signals;

namespace NetSdrMonitor.IntegrationTests.Communication;

/// <summary>
/// Наскрізні тести проти бойового мок-сервера (того, що крутить демо/застосунок):
/// SdrMonitor ← MockLoopbackTransportFactory (TCP) → MockSignalServer. Доводять, що зв'язка
/// «фабрика піднімає сервер на loopback — клієнт під'єднується — сигнали течуть» працює цілком,
/// і що монітор лишається стійким, коли мок підмішує биті/чужі кадри та обриви.
/// </summary>
public sealed class MockSignalServerIntegrationTests
{
    [Fact]
    public async Task CleanServer_StreamsDecodableSignals()
    {
        // хаос вимкнено (ймовірності за замовчуванням нульові) — чистий потік валідних сигналів
        await using SdrMonitor monitor = BuildMonitor(
                    generatorSeed: 123,
                    new MockSignalServerOptions
                    {
                                SendInterval = TimeSpan.FromMilliseconds(20),
                                ChaosSeed    = 1,
                    });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        monitor.Start();

        List<Signal> received = await CollectAsync(monitor, count: 5, cts.Token);

        Assert.Equal(5, received.Count);
        Assert.All(received, AssertPlausible); // декодовані значення в межах генератора
    }

    [Fact]
    public async Task NoisyServer_StaysResilient_AndKeepsDeliveringSignals()
    {
        // вмикаємо весь «хаос»: биті кадри, непідтримувані control item-и та зрідка обриви
        await using SdrMonitor monitor = BuildMonitor(
                    generatorSeed: 999,
                    new MockSignalServerOptions
                    {
                                SendInterval              = TimeSpan.FromMilliseconds(10),
                                MalformedFrameProbability = 0.20, // парсер відхиляє — не сигнал
                                UnknownControlProbability = 0.10, // монітор відповідає NAK
                                DropProbability           = 0.01, // зрідка обрив => монітор має перепідключитись
                                ChaosSeed                 = 12_345,
                    });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        monitor.Start();

        // монітор має проковтнути шум, пережити обриви й усе одно віддати валідні сигнали
        List<Signal> received = await CollectAsync(monitor, count: 5, cts.Token);

        Assert.Equal(5, received.Count);
        Assert.All(received, AssertPlausible);
    }

    private static SdrMonitor BuildMonitor(int generatorSeed, MockSignalServerOptions serverOptions)
    {
        var generator = new RandomSignalGenerator(generatorSeed);
        var server = new MockSignalServer(
                    new IPEndPoint(IPAddress.Loopback, 0), // порт 0 => ОС обере вільний; фабрика зчитає реальний
                    generator,
                    NullLogger<MockSignalServer>.Instance,
                    serverOptions);

        // фабрика володіє сервером: монітор діспозить її разом із собою => мок гаситься на DisposeAsync монітора
        var factory = new MockLoopbackTransportFactory(server);
        return new SdrMonitor(
                    NullLogger<SdrMonitor>.Instance,
                    factory,
                    new SdrMonitorOptions
                    {
                                ConnectTimeout = TimeSpan.FromSeconds(2),
                                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                                IdleTimeout    = TimeSpan.FromSeconds(30),
                    });
    }

    // Читає з каналу монітора рівно count сигналів або повертає зібране при таймауті (assert покаже причину).
    private async static Task<List<Signal>> CollectAsync(ISdrMonitor monitor, int count, CancellationToken ct)
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
            // дедлайн вичерпано — віддаємо часткове
        }

        return received;
    }

    // Декодований сигнал має лежати в межах, які закладає RandomSignalGenerator (інакше це не справжній сигнал).
    private static void AssertPlausible(Signal signal)
    {
        // станція 1..120 МГц (типовий діапазон генератора) + джитер у межах смуги (макс смуга 50 кГц => |джитер| < 25 кГц)
        Assert.InRange(signal.FrequencyHz, 1_000_000UL - 50_000, 120_000_000UL + 50_000);
        Assert.Contains(signal.BandwidthHz, new uint[] { 5_000, 10_000, 25_000, 50_000 });
        Assert.InRange(signal.SnrDb, 5.0, 40.0);
    }
}
