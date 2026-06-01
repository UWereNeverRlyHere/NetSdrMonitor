using NetSdrMonitor.Domain.Signals;

namespace NetSdrMonitor.Domain.Aggregation;

/// <summary>
/// Будівник <see cref="SignalAggregator"/>: дозволяє декларативно під'єднати реакції на події
/// агрегації (відкриття/оновлення/закриття запису, завершення потоку), щоб викликаючий код
/// був вільним від зайвих if-ів і сам лише «штовхав» сигнали через Process.
/// </summary>
public sealed class SignalAggregatorBuilder
{
   private Action<SignalRecord>?                    _onRecordOpened;
   private Action<SignalRecord, Signal>?            _onSignalAppended;
   private Action<SignalRecord, RecordCloseReason>? _onRecordClosed;
   private Action?                                  _onCompleted;

   /// <summary>
   /// Відкрито НОВИЙ запис (перший сигнал або сигнал поза діапазоном попереднього).
   /// Типовий кейс UI — додати новий рядок у таблицю.
   /// </summary>
   public SignalAggregatorBuilder OnRecordOpened(Action<SignalRecord> handler)
   {
      _onRecordOpened = handler;
      return this;
   }

   /// <summary>
   /// Сигнал влився у ПОТОЧНИЙ відкритий запис (Count++ і медіана оновились).
   /// Типовий кейс UI — оновити вже наявний рядок.
   /// </summary>
   public SignalAggregatorBuilder OnSignalAppended(Action<SignalRecord, Signal> handler)
   {
      _onSignalAppended = handler;
      return this;
   }

   /// <summary>
   /// Запис закрито. Причина розрізняє витіснення новим сигналом і завершення потоку.
   /// Типовий кейс — зберегти запис у репозиторій / зафіксувати у консолі.
   /// </summary>
   public SignalAggregatorBuilder OnRecordClosed(Action<SignalRecord, RecordCloseReason> handler)
   {
      _onRecordClosed = handler;
      return this;
   }

   /// <summary>
   /// Потік повністю завершено (Flush/Dispose). Викликається рівно один раз.
   /// Типовий кейс — прибрати індикатор «йде прийом», фінальний підрахунок.
   /// </summary>
   public SignalAggregatorBuilder OnCompleted(Action handler)
   {
      _onCompleted = handler;
      return this;
   }

   /// <summary>
   /// Збирає налаштований агрегатор.
   /// </summary>
   public SignalAggregator Build() => new(_onRecordOpened, _onSignalAppended, _onRecordClosed, _onCompleted);
}
