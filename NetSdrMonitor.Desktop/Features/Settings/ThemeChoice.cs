using NetSdrMonitor.Desktop.Settings;

namespace NetSdrMonitor.Desktop.Features.Settings;

/// <summary>
/// Пункт випадного списку вибору теми: значення режиму та його підпис українською.
/// </summary>
public sealed record ThemeChoice
{
   public required AppTheme Value   { get; init; }
   public required string   Display { get; init; }
}
