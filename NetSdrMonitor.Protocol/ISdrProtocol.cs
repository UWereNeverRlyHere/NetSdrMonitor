using System.Buffers;
using NetSdrMonitor.Protocol.Messages;

namespace NetSdrMonitor.Protocol;

/// <summary>
/// Правила протоколу NetSDR: вирішує, коли повідомлення зібралося в буфері (framing),
/// складає з байтів готовий <see cref="SdrMessage"/> та визначає відповідь (ACK/NAK).
/// Не перетворює повідомлення на сигнал - це робота парсера.
/// </summary>
public interface ISdrProtocol
{
    /// <summary>
    /// Аналізує вхідний буфер і повертає статус: Ready (зібралося ціле повідомлення),
    /// Incomplete (бракує байтів) або Corrupt (структурно зіпсовано - потрібен ресинк).
    /// </summary>
    SdrAnalyzeContext Analyze(ReadOnlySequence<byte> buffer);

    /// <summary>
    /// Дістає готове повідомлення з початку буфера. Передумова: попередній <see cref="Analyze"/>
    /// повернув <see cref="MessageStatus.Ready"/>. Якщо передумову порушено — кидає
    /// <see cref="InvalidOperationException"/> (це помилка викликача, а не штатна гілка).
    /// Копіює потрібні байти в <see cref="SdrMessage.Raw"/>, тож зріз буфера далі можна звільняти.
    /// </summary>
    SdrMessage Extract(SdrAnalyzeContext context, ReadOnlySequence<byte> buffer);

    /// <summary>
    /// Визначає відповідь на вхідне повідомлення згідно з правилами протоколу:
    /// ACK на коректний Data Item, NAK на непідтримуваний Control Item, або нічого (null).
    /// </summary>
    SdrMessage? GetReply(SdrMessage message);

    /// <summary>Збирає команду Run/Stop приймача (бік клієнта).</summary>
    SdrMessage CreateReceiverStateMessage(ReceiverState state);

    /// <summary>
    /// Намагається витлумачити повідомлення як команду Run/Stop приймача (бік сервера).
    /// false — якщо це не Receiver State Control Item. Дзеркало <see cref="CreateReceiverStateMessage"/>.
    /// </summary>
    bool TryGetReceiverState(SdrMessage message, out ReceiverState state);
}
