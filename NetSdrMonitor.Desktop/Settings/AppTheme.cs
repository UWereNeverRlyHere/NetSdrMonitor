namespace NetSdrMonitor.Desktop.Settings;

/// <summary>
/// Режим теми оформлення застосунку, який обирає користувач у налаштуваннях.
/// </summary>
public enum AppTheme
{
    /// <summary>
    /// Слідувати за поточною темою Windows (світла/темна) і змінюватися разом із нею.
    /// </summary>
    System,

    /// <summary>
    /// Завжди світла тема, незалежно від системної.
    /// </summary>
    Light,

    /// <summary>
    /// Завжди темна тема, незалежно від системної.
    /// </summary>
    Dark,
}
