namespace NetSdrMonitor.Desktop.Settings;

/// <summary>
/// Збережене розташування вікна: «нормальні» межі (до розгортання) та ознака розгорнутого стану.
/// Координати зберігаємо як є — їх інтерпретує сама ОС під час відновлення.
/// </summary>
public sealed record WindowPlacement
{
    public required int Left { get; init; }
    public required int Top { get; init; }
    public required int Right { get; init; }
    public required int Bottom { get; init; }
    public required bool IsMaximized { get; init; }
}
