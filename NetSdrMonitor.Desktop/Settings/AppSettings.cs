using NetSdrMonitor.Communication.Monitor;
using NetSdrMonitor.Communication.Server;

namespace NetSdrMonitor.Desktop.Settings;

/// <summary>
/// Налаштування застосунку, що зберігаються у файлі поруч із виконуваним файлом.
/// </summary>
public sealed record AppSettings
{
   public SdrMonitorOptions Monitor { get; init; } = new();
   public MockSignalServerOptions Mock { get; init; } = new();
   public bool HideMainWindowOnStartup { get; init; }
   public IReadOnlyList<ColumnSetting> Columns { get; init; } = [];
}

/// <summary>
/// Збережений стан колонки таблиці головного вікна (порядок і ширина).
/// </summary>
public sealed record ColumnSetting
{
   public required string Key   { get; init; }
   public required int    Order { get; init; }
   public required double Width { get; init; }
}
