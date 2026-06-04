using CommunityToolkit.Mvvm.ComponentModel;
using NetSdrMonitor.Desktop.Settings;

namespace NetSdrMonitor.Desktop.Features.Settings;

/// <summary>
/// Редаговані поля вікна налаштувань. Таймаути — у секундах, інтервал видачі — у мілісекундах.
/// </summary>
public sealed partial class SettingsViewModel(AppSettings source) : ObservableObject
{
   [ObservableProperty] private double _idleTimeoutSeconds = source.Monitor.IdleTimeout.TotalSeconds;
   [ObservableProperty] private double _reconnectDelaySeconds = source.Monitor.ReconnectDelay.TotalSeconds;
   [ObservableProperty] private double _connectTimeoutSeconds = source.Monitor.ConnectTimeout.TotalSeconds;
   [ObservableProperty] private double _sendIntervalMs = source.Mock.SendInterval.TotalMilliseconds;
   [ObservableProperty] private double _malformedFrameProbability = source.Mock.MalformedFrameProbability;
   [ObservableProperty] private double _unknownControlProbability = source.Mock.UnknownControlProbability;
   [ObservableProperty] private double _dropProbability = source.Mock.DropProbability;
   [ObservableProperty] private bool   _hideMainWindowOnStartup = source.HideMainWindowOnStartup;
   [ObservableProperty] private bool   _useInMemoryStorage = source.UseInMemoryStorage;
   [ObservableProperty] private bool   _useMedianFrequency = source.UseMedianFrequency;
   [ObservableProperty] private int    _maxUiRecords = source.MaxUiRecords;
   [ObservableProperty] private bool   _showConsole = source.ShowConsole;
   [ObservableProperty] private AppTheme _theme = source.Theme;

   /// <summary>
   /// Варіанти теми для випадного списку (підпис українською, значення — режим).
   /// </summary>
   public IReadOnlyList<ThemeChoice> ThemeOptions { get; } =
   [
      new() { Value = AppTheme.Light,  Display = "Світла" },
      new() { Value = AppTheme.Dark,   Display = "Темна" },
      new() { Value = AppTheme.System, Display = "Синхронізувати з ОС" },
   ];

   /// <summary>
   /// Збирає оновлені налаштування з полів, зберігаючи решту значень джерела.
   /// </summary>
   public AppSettings ToSettings() => source with
   {
         Monitor = source.Monitor with
         {
               IdleTimeout    = TimeSpan.FromSeconds(IdleTimeoutSeconds),
               ReconnectDelay = TimeSpan.FromSeconds(ReconnectDelaySeconds),
               ConnectTimeout = TimeSpan.FromSeconds(ConnectTimeoutSeconds),
         },
         Mock = source.Mock with
         {
               SendInterval              = TimeSpan.FromMilliseconds(SendIntervalMs),
               MalformedFrameProbability = MalformedFrameProbability,
               UnknownControlProbability = UnknownControlProbability,
               DropProbability           = DropProbability,
         },
         HideMainWindowOnStartup = HideMainWindowOnStartup,
         UseInMemoryStorage      = UseInMemoryStorage,
         UseMedianFrequency      = UseMedianFrequency,
         MaxUiRecords            = MaxUiRecords,
         ShowConsole             = ShowConsole,
         Theme                   = Theme,
   };
}
