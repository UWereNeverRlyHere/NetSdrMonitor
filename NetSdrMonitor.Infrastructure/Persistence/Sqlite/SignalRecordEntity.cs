namespace NetSdrMonitor.Infrastructure.Persistence.Sqlite;

/// <summary>
/// Рядок таблиці записів — модель зберігання, а не домен. Тримає лише сурогатний ключ,
/// знімок стану «закритий» та свої сигнали; багату модель відновлюємо при читанні.
/// </summary>
public sealed class SignalRecordEntity
{
    /// <summary>
    /// Сурогатний первинний ключ (автоінкремент).
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Чи був запис закритий на момент збереження.
    /// </summary>
    public bool IsClosed { get; set; }

    /// <summary>
    /// Сигнали запису в порядку надходження (для відновлення лічильника й медіани).
    /// </summary>
    public List<SignalEntity> Signals { get; } = [];
}
