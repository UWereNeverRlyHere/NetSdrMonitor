using Microsoft.Extensions.Logging;
using NetSdrMonitor.Desktop.Features.Console;

namespace NetSdrMonitor.Desktop.Logging;

/// <summary>
/// Провайдер логерів для UI-консолі: усі категорії пишуть в один <see cref="UiLogSink"/>.
/// Конкретику логування (куди й як пишемо) знає лише композиційний корінь — як і годиться.
/// </summary>
public sealed class UiLoggerProvider(UiLogSink sink, LogLevel minLevel) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new UiLogger(categoryName, sink, minLevel);

    public void Dispose()
    {
        // окремих ресурсів немає
    }
}
