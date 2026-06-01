namespace NetSdrMonitor.Domain.Aggregation;

/// <summary>
/// Причина, з якої запис було закрито. Дає підписникам контекст,
/// щоб не плутати штатне витіснення новим сигналом із завершенням потоку.
/// </summary>
public enum RecordCloseReason : byte
{
   /// <summary>
   /// Прийшов сигнал поза діапазоном — запис витіснено новим.
   /// </summary>
   OutOfRange,

   /// <summary>
   /// Потік завершився (Flush/Dispose) — закриваємо останній відкритий запис.
   /// </summary>
   StreamCompleted,
}
