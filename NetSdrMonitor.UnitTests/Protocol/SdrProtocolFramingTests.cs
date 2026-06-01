using System.Buffers;
using NetSdrMonitor.Domain.Signals;
using NetSdrMonitor.Protocol;
using NetSdrMonitor.Protocol.Messages;

namespace NetSdrMonitor.UnitTests.Protocol;

/// <summary>
/// Тести framing'у: round-trip 2-байтового заголовка та статуси Analyze (Ready/Incomplete/Corrupt),
/// у тому числі битий хедер, плюс зв'язка Analyze→Extract→сигнал.
/// </summary>
public sealed class SdrProtocolFramingTests
{
    private readonly SdrProtocol _protocol = new();

    [Theory]
    [InlineData(SdrMessageType.DataItem0, 30)]
    [InlineData(SdrMessageType.SetControlItem, 8)]
    [InlineData(SdrMessageType.DataItemAck, 3)]
    [InlineData(SdrMessageType.DataItem0, 8194)] // спецвипадок: 8194 кодується як 0 і назад
    public void Header_WriteTo_ThenParse_RoundTrips(SdrMessageType type, int totalLength)
    {
        SdrMessageHeader original = new() { Type = type, Length = (ushort)totalLength };

        Span<byte> two = stackalloc byte[SdrMessageHeader.Size];
        original.WriteTo(two);
        SdrMessageHeader parsed = SdrMessageHeader.Parse(two);

        Assert.Equal(original.Type, parsed.Type);
        Assert.Equal(original.Length, parsed.Length);
    }

    [Fact]
    public void Analyze_ReturnsIncomplete_WhenFewerThanHeaderBytes()
    {
        var buffer = new ReadOnlySequence<byte>(new byte[] { 0x05 }); // лише 1 байт із 2

        SdrAnalyzeContext context = _protocol.Analyze(buffer);

        Assert.Equal(MessageStatus.Incomplete, context.Status);
    }

    [Fact]
    public void Analyze_ReturnsIncomplete_WhenBodyNotFullyArrived()
    {
        // заголовок каже «30 байт», а в буфері лише 10
        SdrMessageHeader header = SdrMessageHeader.FromPayload(SdrMessageType.DataItem0, payloadLength: 28);
        var bytes = new byte[10];
        header.WriteTo(bytes);

        SdrAnalyzeContext context = _protocol.Analyze(new ReadOnlySequence<byte>(bytes));

        Assert.Equal(MessageStatus.Incomplete, context.Status);
    }

    [Fact]
    public void Analyze_ReturnsCorrupt_WhenLengthSmallerThanHeader()
    {
        // байти кодують довжину 1 (< 2) — структурно неможливий кадр (битий хедер)
        var buffer = new ReadOnlySequence<byte>([0x01, 0x00]);

        SdrAnalyzeContext context = _protocol.Analyze(buffer);

        Assert.Equal(MessageStatus.Corrupt, context.Status);
    }

    [Fact]
    public void Analyze_ReturnsReady_AndExtractRoundTripsSignal()
    {
        var parser = new SdrMessageParser();
        Signal original = new()
        {
            TimestampUnixMs = 123,
            FrequencyHz     = 14_010_000,
            BandwidthHz     = 25_000,
            SnrDb           = 7.5,
        };
        byte[] frame = parser.FromSignal(original).Raw.ToArray();
        var buffer = new ReadOnlySequence<byte>(frame);

        SdrAnalyzeContext context = _protocol.Analyze(buffer);
        Assert.Equal(MessageStatus.Ready, context.Status);
        Assert.Equal(SdrMessageType.DataItem0, context.Header.Type);

        SdrMessage message = _protocol.Extract(context, buffer);
        Assert.True(parser.TryToSignal(message, out Signal decoded));
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Extract_Throws_WhenContextNotReady()
    {
        var corruptBytes = new ReadOnlySequence<byte>(new byte[] { 0x01, 0x00 });
        SdrAnalyzeContext notReady = _protocol.Analyze(buffer: corruptBytes); // Corrupt

        Assert.Throws<InvalidOperationException>(() => _protocol.Extract(notReady, corruptBytes));
    }
}
