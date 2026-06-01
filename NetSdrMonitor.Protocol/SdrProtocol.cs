using System.Buffers;
using System.Buffers.Binary;
using NetSdrMonitor.Protocol.ControlItems;
using NetSdrMonitor.Protocol.Messages;

namespace NetSdrMonitor.Protocol;

/// <summary>
/// Реалізація правил NetSDR: framing (коли повідомлення зібралось), складання готового
/// <see cref="SdrMessage"/> з байтів і визначення відповіді (ACK/NAK). Сигналів не торкається.
/// </summary>
public sealed class SdrProtocol : ISdrProtocol
{
    private const int ControlCodeSize = 2; // 16-бітний Control Item Code одразу після заголовка

    // run/stop байти Receiver State зі специфікації (приховані від решти коду)
    private const byte RunByte = 0x02;
    private const byte StopByte = 0x01;

    /// <inheritdoc />
    public MessageStatus Analyze(ReadOnlySequence<byte> buffer)
    {
        // ще немає навіть заголовка
        if (buffer.Length < SdrMessageHeader.Size)
            return MessageStatus.Incomplete;

        SdrMessageHeader header = ReadHeader(buffer);

        // довжина менша за сам заголовок — структурно неможливо
        if (header.Length < SdrMessageHeader.Size)
            return MessageStatus.Corrupt;

        // цілий кадр ще не прийшов
        if (buffer.Length < header.Length)
            return MessageStatus.Incomplete;

        return MessageStatus.Ready;
    }

    /// <inheritdoc />
    public SdrMessage Extract(ReadOnlySequence<byte> buffer)
    {
        // передумова: цілий кадр уже в буфері (Analyze == Ready). Порушення — баг викликача.
        if (Analyze(buffer) != MessageStatus.Ready)
            throw new InvalidOperationException("Extract викликано без статусу Ready від Analyze.");

        SdrMessageHeader header = ReadHeader(buffer);

        // копіюємо рівно один кадр у власний масив — далі зріз буфера можна звільняти
        byte[] raw = buffer.Slice(0, header.Length).ToArray();

        ControlItemCode? code = TryReadControlCode(header, raw);

        return new SdrMessage
        {
            Header = header,
            Raw = raw,
            ControlCode = code,
        };
    }

    /// <inheritdoc />
    public SdrMessage? GetReply(SdrMessage message)
    {
        // на коректний потік даних відповідаємо ACK відповідного каналу
        if (message.Header.Type is >= SdrMessageType.DataItem0 and <= SdrMessageType.DataItem3)
        {
            byte channel = message.Header.Type - SdrMessageType.DataItem0;
            return CreateAck(channel);
        }

        // на непідтримуваний Control Item — NAK (підтримуємо лише Receiver State)
        if (IsControlItem(message.Header.Type) && message.ControlCode != ControlItemCode.ReceiverState)
            return CreateNak();

        return null;
    }

    /// <inheritdoc />
    public SdrMessage CreateReceiverStateMessage(ReceiverState state)
    {
        byte runStop = state == ReceiverState.Running ? RunByte : StopByte;

        // тіло Set Control Item: [code(2)] + [канал/тип][run/stop][режим][N]
        ReadOnlySpan<byte> parameters = [0x00, runStop, 0x00, 0x00];
        return BuildControlItem(SdrMessageType.SetControlItem, ControlItemCode.ReceiverState, parameters);
    }

    // --- приватна кухня ---

    private static SdrMessageHeader ReadHeader(ReadOnlySequence<byte> buffer)
    {
        // заголовок може лягти на стик сегментів — копіюємо 2 байти у безперервний буфер на стеку
        Span<byte> headerBytes = stackalloc byte[SdrMessageHeader.Size];
        buffer.Slice(0, SdrMessageHeader.Size).CopyTo(headerBytes);
        return SdrMessageHeader.Parse(headerBytes);
    }

    private static ControlItemCode? TryReadControlCode(SdrMessageHeader header, ReadOnlySpan<byte> raw)
    {
        if (!IsControlItem(header.Type) || header.PayloadLength < ControlCodeSize)
            return null;

        ushort code = BinaryPrimitives.ReadUInt16LittleEndian(raw[SdrMessageHeader.Size..]);
        return (ControlItemCode)code;
    }

    // Control Item-и — це типи host→target / target→host зі значенням 0..2
    private static bool IsControlItem(SdrMessageType type) =>
        type <= SdrMessageType.RequestControlItemRange;

    private static SdrMessage BuildControlItem(SdrMessageType type, ControlItemCode code, ReadOnlySpan<byte> parameters)
    {
        int payloadLength = ControlCodeSize + parameters.Length;
        SdrMessageHeader header = SdrMessageHeader.FromPayload(type, payloadLength);

        var raw = new byte[header.Length];
        header.WriteTo(raw);
        Span<byte> body = raw.AsSpan(SdrMessageHeader.Size);
        BinaryPrimitives.WriteUInt16LittleEndian(body, (ushort)code);
        parameters.CopyTo(body[ControlCodeSize..]);

        return new SdrMessage { Header = header, Raw = raw, ControlCode = code };
    }

    private static SdrMessage CreateNak()
    {
        // NAK — заголовок довжини 2 без тіла (тип 0). WriteTo дає байти [02][00].
        SdrMessageHeader header = SdrMessageHeader.FromPayload(SdrMessageType.SetControlItem, 0);
        var raw = new byte[header.Length];
        header.WriteTo(raw);
        return new SdrMessage { Header = header, Raw = raw };
    }

    private static SdrMessage CreateAck(byte dataItemChannel)
    {
        // ACK Data Item — заголовок (тип DataItemAck, довжина 3) + 1 байт номера каналу.
        SdrMessageHeader header = SdrMessageHeader.FromPayload(SdrMessageType.DataItemAck, 1);
        var raw = new byte[header.Length];
        header.WriteTo(raw);
        raw[SdrMessageHeader.Size] = dataItemChannel;
        return new SdrMessage { Header = header, Raw = raw };
    }
}
