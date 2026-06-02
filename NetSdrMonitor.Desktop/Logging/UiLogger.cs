using Microsoft.Extensions.Logging;
using NetSdrMonitor.Desktop.Features.Console;

namespace NetSdrMonitor.Desktop.Logging;

/// <summary>
/// Логер, що форматує повідомлення й кладе їх у <see cref="UiLogSink"/> для показу в консолі.
/// Рівні нижчі за поріг (зокрема Trace «на кадр») відсікаються — щоб консоль показувала суть,
/// а не потоковий шум.
/// </summary>
public sealed class UiLogger : ILogger
{
   private readonly string _source;
   private readonly UiLogSink _sink;
   private readonly LogLevel _minLevel;

   public UiLogger(string category, UiLogSink sink, LogLevel minLevel)
   {
      // у консолі показуємо короткий тип (останній сегмент категорії), а не повну назву
      int lastDot = category.LastIndexOf('.');
      _source   = lastDot >= 0 ? category[(lastDot + 1)..] : category;
      _sink     = sink;
      _minLevel = minLevel;
   }

   public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

   public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= _minLevel;

   public void Log<TState>(
      LogLevel                        logLevel,
      EventId                         eventId,
      TState                          state,
      Exception?                      exception,
      Func<TState, Exception?, string> formatter)
   {
      if (!IsEnabled(logLevel))
         return;

      string message = formatter(state, exception);
      if (exception is not null)
         message = $"{message} — {exception.Message}";

      _sink.Write(new LogEntry
      {
         Timestamp = DateTime.Now,
         Level     = logLevel,
         Source    = _source,
         Message   = message,
      });
   }
}
