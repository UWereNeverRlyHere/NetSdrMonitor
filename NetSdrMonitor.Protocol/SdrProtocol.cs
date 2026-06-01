using System.Buffers;
using System.Buffers.Binary;
using NetSdrMonitor.Protocol.ControlItems;
using NetSdrMonitor.Protocol.Messages;

namespace NetSdrMonitor.Protocol;

public readonly record struct SdrAnalyzeContext
{
   public required MessageStatus Status { get; init; }
   public SdrMessageHeader Header { get; init; }
}

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
    public SdrAnalyzeContext Analyze(ReadOnlySequence<byte> buffer)
    {
        // ще немає навіть заголовка
        if (buffer.Length < SdrMessageHeader.Size)
            return new() { Status = MessageStatus.Incomplete };

        SdrMessageHeader header = ReadHeader(buffer);

        // довжина менша за сам заголовок — структурно неможливо
        if (header.Length < SdrMessageHeader.Size)
            return new() { Status = MessageStatus.Corrupt, Header = header};

        // цілий кадр ще не прийшов
        if (buffer.Length < header.Length)
           return new() { Status = MessageStatus.Incomplete, Header = header};

        return new(){Status = MessageStatus.Ready, Header = header};
    }

    /// <inheritdoc />
    public SdrMessage Extract(SdrAnalyzeContext context,ReadOnlySequence<byte> buffer)
    {
        // передумова: цілий кадр уже в буфері (Analyze == Ready). Порушення — баг викликача.
        if (context.Status != MessageStatus.Ready || context.Header.Length == 0)
            throw new InvalidOperationException("Extract викликано без статусу Ready.");

        SdrMessageHeader header = context.Header;
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
            // Якщо протокол потребуватиме чіткого ACK на ці поля...
            return null;
            //return CreateAck(channel);
        }

        // NAK лише на справжній Control Item з кодом (Set/Request), якого не підтримуємо.
        // Повідомлення без коду (зокрема сам NAK) не неквируємо — інакше дві сторони
        // зациклились би у відповідях NAK-на-NAK.
        if (IsControlItem(message.Header.Type)
            && message.ControlCode is { } code
            && code != ControlItemCode.ReceiverState)
            return CreateNak();

        return null;
    }

    /// <inheritdoc />
    public SdrMessage CreateReceiverStateMessage(ReceiverState state)
    {
        byte runStop = state == ReceiverState.Running ? RunByte : StopByte;

        // тіло Set Control Item: [code(2)] + [канал/тип][run/stop][режим][N]
        ReadOnlySpan<byte> parameters = [0x00, runStop, 0x00, 0x00];

        var type = SdrMessageType.SetControlItem;
        var code = ControlItemCode.ReceiverState;
        
        int payloadLength = ControlCodeSize + parameters.Length;
        SdrMessageHeader header = SdrMessageHeader.FromPayload(type, payloadLength);

        var raw = new byte[header.Length];
        header.WriteTo(raw);
        Span<byte> body = raw.AsSpan(SdrMessageHeader.Size);
        BinaryPrimitives.WriteUInt16LittleEndian(body, (ushort)code);
        parameters.CopyTo(body[ControlCodeSize..]);

        return new SdrMessage { Header = header, Raw = raw, ControlCode = code };
    }

    /// <inheritdoc />
    public bool TryGetReceiverState(SdrMessage message, out ReceiverState state)
    {
        state = default;

        if (!IsControlItem(message.Header.Type) || message.ControlCode != ControlItemCode.ReceiverState)
            return false;

        // тіло: [code(2)][канал][run/stop][...]; байт run/stop іде третім після коду
        const int runStopOffset = ControlCodeSize + 1;
        ReadOnlySpan<byte> body = message.Payload.Span;
        if (body.Length <= runStopOffset)
            return false;

        switch (body[runStopOffset])
        {
            case RunByte:  state = ReceiverState.Running; return true;
            case StopByte: state = ReceiverState.Stopped; return true;
            default:       return false;
        }
    }

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

    // Control Item-и - це типи host→target / target -> host зі значенням 0..2
    private static bool IsControlItem(SdrMessageType type) => type <= SdrMessageType.RequestControlItemRange;
    
    private static SdrMessage CreateNak()
    {
        // NAK - заголовок довжини 2 без тіла (тип 0). WriteTo дає байти [02][00].
        SdrMessageHeader header = SdrMessageHeader.FromPayload(SdrMessageType.SetControlItem, 0);
        var raw = new byte[header.Length];
        header.WriteTo(raw);
        return new SdrMessage { Header = header, Raw = raw };
    }

    private static SdrMessage CreateAck(byte dataItemChannel)
    {
        // ACK Data Item - заголовок (тип DataItemAck, довжина 3) + 1 байт номера каналу.
        SdrMessageHeader header = SdrMessageHeader.FromPayload(SdrMessageType.DataItemAck, 1);
        var raw = new byte[header.Length];
        header.WriteTo(raw);
        raw[SdrMessageHeader.Size] = dataItemChannel;
        return new SdrMessage { Header = header, Raw = raw };
    }
}
