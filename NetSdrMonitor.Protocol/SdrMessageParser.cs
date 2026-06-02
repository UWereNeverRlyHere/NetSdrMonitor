using System.Buffers.Binary;
using NetSdrMonitor.Domain.Signals;
using NetSdrMonitor.Protocol.Messages;

namespace NetSdrMonitor.Protocol;

/// <summary>
/// Реалізація парсера: маппінг готового <see cref="SdrMessage"/> - доменного <see cref="Signal"/>.
/// </summary>
public sealed class SdrMessageParser : ISdrMessageParser
{
    private const int TimestampOffset = 0;  // uint64
    private const int FrequencyOffset = 8;  // uint64
    private const int BandwidthOffset = 16; // uint32
    private const int SnrOffset = 20;       // double (8 байтів)
    private const int SignalPayloadSize = 28;

    /// <inheritdoc />
    public bool TryToSignal(SdrMessage message, out Signal signal)
    {
        signal = null!;

        // сигнал - це лише канал даних 0 з рівно очікуваним розміром тіла
        if (message.Header.Type != SdrMessageType.DataItem0 || message.Payload.Length != SignalPayloadSize)
            return false;

        ReadOnlySpan<byte> body = message.Payload.Span;

        // хибне трактування байтів (порядок/тип) дає NaN/Inf — це не валідний рівень сигналу
        double snrDb = BinaryPrimitives.ReadDoubleLittleEndian(body[SnrOffset..]);
        if (!double.IsFinite(snrDb))
            return false;

        signal = new Signal
        {
                    TimestampUnixMs = BinaryPrimitives.ReadInt64LittleEndian(body[TimestampOffset..]),
                    FrequencyHz     = BinaryPrimitives.ReadUInt64LittleEndian(body[FrequencyOffset..]),
                    BandwidthHz     = BinaryPrimitives.ReadUInt32LittleEndian(body[BandwidthOffset..]),
                    SnrDb           = snrDb,
        };

        return true;
    }

    /// <inheritdoc />
    public SdrMessage FromSignal(Signal signal)
    {
        SdrMessageHeader header = SdrMessageHeader.FromPayload(SdrMessageType.DataItem0, SignalPayloadSize);

        var raw = new byte[header.Length];
        header.WriteTo(raw);

        Span<byte> body = raw.AsSpan(SdrMessageHeader.Size);
        BinaryPrimitives.WriteInt64LittleEndian(body[TimestampOffset..], signal.TimestampUnixMs);
        BinaryPrimitives.WriteUInt64LittleEndian(body[FrequencyOffset..], signal.FrequencyHz);
        BinaryPrimitives.WriteUInt32LittleEndian(body[BandwidthOffset..], signal.BandwidthHz);
        BinaryPrimitives.WriteDoubleLittleEndian(body[SnrOffset..], signal.SnrDb);

        return new SdrMessage
        {
                    Header = header,
                    Raw    = raw
        };
    }
}
