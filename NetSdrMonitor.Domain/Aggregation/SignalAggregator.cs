using NetSdrMonitor.Domain.Signals;

namespace NetSdrMonitor.Domain.Aggregation;

/// <summary>
/// Оркестратор потоку детекцій. Тримає один «відкритий» запис і прогоняє крізь нього сигнали,
/// сповіщаючи підписників про події (відкрито / оновлено / закрито / завершено).
/// Уся логіка «попав/не попав» живе у <see cref="SignalRecord"/> — тут лише оркестрація та виклик колбеків.
/// </summary>
/// <remarks>
/// Послідовна модель: новий сигнал порівнюємо ЛИШЕ з поточним відкритим записом.
/// <para>
/// Реалізує <see cref="IDisposable"/>, бо власник зобов'язаний завершити потік: Dispose закриває
/// останній відкритий запис (інакше його дані «загубляться») і викликає OnCompleted рівно один раз.
/// </para>
/// Створюється через <see cref="SignalAggregatorBuilder"/> (метод <see cref="Create"/>).
/// Не потокобезпечний: подавати сигнали слід з одного потоку (або під зовнішнім локом).
/// </remarks>
public sealed class SignalAggregator : IDisposable
{
   private readonly Action<SignalRecord>?                    _onRecordOpened;
   private readonly Action<SignalRecord, Signal>?            _onSignalAppended;
   private readonly Action<SignalRecord, RecordCloseReason>? _onRecordClosed;
   private readonly Action?                                  _onCompleted;

   private SignalRecord? _current;
   private bool          _isCompleted;

   internal SignalAggregator(
      Action<SignalRecord>?                    onRecordOpened,
      Action<SignalRecord, Signal>?            onSignalAppended,
      Action<SignalRecord, RecordCloseReason>? onRecordClosed,
      Action?                                  onCompleted)
   {
      _onRecordOpened   = onRecordOpened;
      _onSignalAppended = onSignalAppended;
      _onRecordClosed   = onRecordClosed;
      _onCompleted      = onCompleted;
   }

   /// <summary>
   /// Починає конфігурування агрегатора через флюент-білдер.
   /// </summary>
   public static SignalAggregatorBuilder Create() => new();

   /// <summary>Поточний відкритий запис (null, доки не прийшов перший сигнал або після завершення).</summary>
   public SignalRecord? Current => _current;

   /// <summary>
   /// Подає сигнал в оркестратор і запускає відповідні колбеки:
   /// перший сигнал чи сигнал поза діапазоном → закриває попередній (OnRecordClosed) та відкриває
   /// новий (OnRecordOpened); сигнал у межах діапазону → оновлює поточний (OnSignalAppended).
   /// </summary>
   public void Process(Signal signal)
   {
      ObjectDisposedException.ThrowIf(_isCompleted, this);

      // перший сигнал у потоці — просто відкриваємо запис
      if (_current is null)
      {
         OpenRecord(signal);
         return;
      }

      // влився у поточний — оновлення наявного рядка
      if (_current.TryAppend(signal))
      {
         _onSignalAppended?.Invoke(_current, signal);
         return;
      }

      // поза діапазоном — закриваємо поточний і відкриваємо новий
      CloseCurrent(RecordCloseReason.OutOfRange);
      OpenRecord(signal);
   }

   /// <summary>
   /// Завершує потік: закриває останній відкритий запис (OnRecordClosed зі StreamCompleted)
   /// і викликає OnCompleted. Викликається з Dispose; повторні виклики безпечні (ідемпотентно).
   /// </summary>
   public void Complete()
   {
      if (_isCompleted)
         return;

      CloseCurrent(RecordCloseReason.StreamCompleted);
      _isCompleted = true;
      _onCompleted?.Invoke();
   }

   /// <summary>Завершує потік (див. <see cref="Complete"/>).</summary>
   public void Dispose() => Complete();

   private void OpenRecord(Signal signal)
   {
      _current = new SignalRecord(signal);
      _onRecordOpened?.Invoke(_current);
   }

   private void CloseCurrent(RecordCloseReason reason)
   {
      if (_current is null)
         return;

      SignalRecord closed = _current;
      closed.Close();
      _current = null;
      _onRecordClosed?.Invoke(closed, reason);
   }
}
