using NetSdrMonitor.Communication.Monitor;
using NetSdrMonitor.Communication.Server;

namespace NetSdrMonitor.Desktop.Settings;

/// <summary>
/// Налаштування застосунку, що зберігаються у файлі поруч із виконуваним файлом.
/// </summary>
public sealed record AppSettings
{
    public SdrMonitorOptions Monitor { get; init; } = new();

    // демо-дефолти: трохи «хаосу», щоб консоль одразу показувала і биті кадри, і обриви/реконекти
    // (саме ядро мока за замовчуванням детерміноване — нулі; вмикає демо тут)
    public MockSignalServerOptions Mock { get; init; } = new()
    {
                MalformedFrameProbability = 0.05,
                UnknownControlProbability = 0.03,
                DropProbability           = 0.01,
    };

    // тема оформлення UI: світла, темна або синхронізація з темою Windows
    public AppTheme Theme { get; init; } = AppTheme.System;

    public bool HideMainWindowOnStartup { get; init; }

    // летке сховище за замовчуванням: файл БД не створюється, поки користувач не обере SQLite
    public bool UseInMemoryStorage { get; init; } = true;

    // частоту показуємо як медіану детекцій запису (інакше — за першим сигналом)
    public bool UseMedianFrequency { get; init; } = true;

    // нижня консоль логів: показувати й яку висоту (px) їй відвести (0 — узяти типову)
    public bool ShowConsole { get; init; } = true;
    public double ConsoleHeight { get; init; }

    // збережений розмір і позиція вікон; null — ще не зберігали, береться стартовий дефолт
    public WindowPlacement? MainWindowPlacement { get; init; }
    public WindowPlacement? SettingsWindowPlacement { get; init; }
    public WindowPlacement? SignalDetailsWindowPlacement { get; init; }

    public IReadOnlyList<ColumnSetting> Columns { get; init; } = [];
}

/// <summary>
/// Збережений стан колонки таблиці головного вікна (порядок і ширина).
/// </summary>
public sealed record ColumnSetting
{
    public required string Key { get; init; }
    public required int Order { get; init; }
    public required double Width { get; init; }
}
