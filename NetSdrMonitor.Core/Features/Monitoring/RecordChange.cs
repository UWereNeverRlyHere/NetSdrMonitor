using NetSdrMonitor.Domain.Aggregation;

namespace NetSdrMonitor.Core.Features.Monitoring;

/// <summary>
/// Вид зміни запису в живому потоці — підказка підписнику UI, що робити з рядком.
/// </summary>
public enum RecordChangeKind : byte
{
    /// <summary>Відкрито новий запис — потрібен новий рядок.</summary>
    Opened,

    /// <summary>Поточний запис поглинув сигнал — оновити наявний рядок.</summary>
    Updated,

    /// <summary>Запис закрито — зафіксувати фінальний стан рядка.</summary>
    Closed,
}

/// <summary>
/// Подія живого потоку записів: який запис змінився і як. UI-агностична — підписник сам
/// маршалить у свій потік і вирішує, як відобразити (а персист уже зробив сервіс).
/// </summary>
public readonly record struct RecordChange
{
    public required SignalRecord Record { get; init; }
    public required RecordChangeKind Kind { get; init; }
}
