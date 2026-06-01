using NetSdrMonitor.Protocol.ControlItems;

namespace NetSdrMonitor.Protocol.Messages;

/// <summary>
/// Фінальне зібране повідомлення NetSDR.
/// </summary>
public sealed record SdrMessage
{
    /// <summary>
    /// Заголовок: тип і повна довжина.
    /// </summary>
    public required SdrMessageHeader Header { get; init; }
    
    public SdrMessageType MessageType => Header.Type;
    
    /// <summary>
    /// Повні сирі байти кадру (заголовок + тіло) — для відправлення «як є».
    /// </summary>
    public required ReadOnlyMemory<byte> Raw { get; init; }

    /// <summary>
    /// Тіло без заголовка (зріз <see cref="Raw"/>).
    /// </summary>
    public ReadOnlyMemory<byte> Payload => Raw[SdrMessageHeader.Size..];

    /// <summary>
    /// Код Control Item, якщо повідомлення керуюче; null — для Data Item (сигнал).
    /// </summary>
    public ControlItemCode? ControlCode { get; init; }
}
