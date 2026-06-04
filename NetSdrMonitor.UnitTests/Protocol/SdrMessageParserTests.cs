using NetSdrMonitor.Communication.Server;
using NetSdrMonitor.Domain.Signals;
using NetSdrMonitor.Protocol;
using NetSdrMonitor.Protocol.ControlItems;
using NetSdrMonitor.Protocol.Messages;

namespace NetSdrMonitor.UnitTests.Protocol;

/// <summary>
/// Тести парсера: round-trip Signal↔SdrMessage та відмови на некоректних вхідних повідомленнях.
/// </summary>
public sealed class SdrMessageParserTests
{
    private readonly SdrMessageParser _parser = new();

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(2026)]
    public void FromSignal_ThenTryToSignal_RoundTripsExactly(int seed)
    {
        var generator = new RandomSignalGenerator(seed);

        // багато сигналів з різними частотами/смугами/SNR — щоб ширше прогнати кодек
        for (int i = 0; i < 200; i++)
        {
            Signal original = generator.Next();

            SdrMessage message = _parser.FromSignal(original);
            bool decodedOk = _parser.TryToSignal(message, out Signal decoded);

            Assert.True(decodedOk);
            Assert.Equal(original, decoded); // record => рівність за всіма полями
        }
    }

    [Fact]
    public void FromSignal_ProducesDataItem0FrameOfExpectedSize()
    {
        Signal signal = new()
        {
            TimestampUnixMs = 1,
            FrequencyHz     = 100_000_000,
            BandwidthHz     = 10_000,
            SnrDb           = 12.5,
        };

        SdrMessage message = _parser.FromSignal(signal);

        Assert.Equal(SdrMessageType.DataItem0, message.Header.Type);
        Assert.Equal(30, message.Raw.Length);     // 2 заголовок + 28 тіло
        Assert.Equal(28, message.Payload.Length);
    }

    [Fact]
    public void TryToSignal_RejectsNonDataItemType()
    {
        // керуюче повідомлення (Run) — це не потік даних
        SdrMessage control = new SdrProtocol().CreateReceiverStateMessage(ReceiverState.Running);

        bool decodedOk = _parser.TryToSignal(control, out Signal signal);

        Assert.False(decodedOk);
        Assert.Null(signal);
    }

    [Fact]
    public void TryToSignal_RejectsDataItem0WithWrongPayloadSize()
    {
        // тип правильний, але тіло не 28 байт — «битий» сигнальний кадр
        SdrMessageHeader header = SdrMessageHeader.FromPayload(SdrMessageType.DataItem0, payloadLength: 8);
        SdrMessage malformed = new() { Header = header, Raw = new byte[header.Length] };

        bool decodedOk = _parser.TryToSignal(malformed, out Signal signal);

        Assert.False(decodedOk);
        Assert.Null(signal);
    }

    [Fact]
    public void TryToSignal_RejectsOtherDataChannels()
    {
        // розмір тіла правильний, але канал 1 — сигналом вважаємо лише Data Item 0
        SdrMessageHeader header = SdrMessageHeader.FromPayload(SdrMessageType.DataItem1, payloadLength: 28);
        SdrMessage otherChannel = new() { Header = header, Raw = new byte[header.Length] };

        bool decodedOk = _parser.TryToSignal(otherChannel, out Signal signal);

        Assert.False(decodedOk);
        Assert.Null(signal);
    }
}
