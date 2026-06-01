namespace NetSdrMonitor.Application.Abstractions.Communication;

/// <summary>
/// Імітатор таргета NetSDR - друга сторона. Приймає клієнтів,
/// реагує на Run/Stop і стрімить згенеровані сигнали. Існує, щоб клієнт можна було
/// запустити й перевірити end-to-end (у тому числі проти зовнішніх інструментів).
/// </summary>
public interface ISignalServer : IAsyncDisposable
{
    /// <summary>
    /// Піднімає слухач і обслуговує клієнтів, доки виклик не скасують.
    /// </summary>
    Task RunAsync(CancellationToken cancellationToken = default);
}
