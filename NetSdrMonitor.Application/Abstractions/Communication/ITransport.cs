using System.Buffers;

namespace NetSdrMonitor.Application.Abstractions.Communication;

/// <summary>
/// Дуплексний потік байтів між хостом і таргетом. Знає лише про байти -
/// ні про кадри, ні про сигнали. Реалізації: TCP (loopback) та in-memory (тести).
/// </summary>
public interface ITransport : IAsyncDisposable
{
    /// <summary>
    /// Чи відкрите з'єднання.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Встановлює з'єднання з таргетом.
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Читає наступну порцію байтів. Повертає весь накопичений буфер (може бути з кількох сегментів);
    /// порожній результат означає, що пір закрив з'єднання.
    /// </summary>
    Task<ReadOnlySequence<byte>> ReceiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Повідомляє транспорт про прогрес обробки буфера:
    /// <paramref name="consumed"/> — байти оброблено повністю, їх можна звільнити;
    /// <paramref name="examined"/> — байти переглянуто (далі не будити, доки не прийде щось нове).
    /// Дві позиції потрібні, щоб уникнути busy-loop на неповному повідомленні.
    /// </summary>
    void AdvanceTo(SequencePosition consumed, SequencePosition examined);

    /// <summary>
    /// Надсилає байти таргету (команди Run/Stop, ACK/NAK тощо).
    /// </summary>
    Task SendAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default);
}
