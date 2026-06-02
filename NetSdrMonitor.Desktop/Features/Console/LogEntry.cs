using Microsoft.Extensions.Logging;

namespace NetSdrMonitor.Desktop.Features.Console;

/// <summary>
/// Один рядок консолі логів: час, рівень, джерело (короткий тип) і повідомлення.
/// Готує також зведений текст рядка для виводу, щоб шаблон лишався простим.
/// </summary>
public sealed record LogEntry
{
   public required DateTime Timestamp { get; init; }
   public required LogLevel Level     { get; init; }
   public required string   Source    { get; init; }
   public required string   Message   { get; init; }

   /// <summary>
   /// Короткий тег рівня для компактного виводу.
   /// </summary>
   public string LevelTag => Level switch
   {
      LogLevel.Trace       => "TRC",
      LogLevel.Debug       => "DBG",
      LogLevel.Information => "INF",
      LogLevel.Warning     => "WRN",
      LogLevel.Error       => "ERR",
      LogLevel.Critical    => "CRT",
      _                    => "—",
   };

   /// <summary>
   /// Цілий рядок «час рівень джерело: повідомлення» для моноширинного виводу.
   /// </summary>
   public string Line => $"{Timestamp:HH:mm:ss.fff}  {LevelTag}  {Source}: {Message}";
}
