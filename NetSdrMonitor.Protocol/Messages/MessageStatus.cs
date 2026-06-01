namespace NetSdrMonitor.Protocol.Messages;

/// <summary>
/// Стан аналізу вхідного буфера протоколом (framing), а не властивість самого повідомлення.
/// </summary>
public enum MessageStatus : byte
{
    /// <summary>
    /// У буфері зібралося ціле повідомлення - можна діставати <see cref="SdrMessage"/>.
    /// </summary>
    Ready,

    /// <summary>
    /// Даних поки замало - треба дочитати ще байтів із сокета.
    /// </summary>
    Incomplete,

    /// <summary>
    /// Кадр структурно неможливий (зіпсована довжина) -потрібен ресинк буфера.
    /// </summary>
    Corrupt,
}
