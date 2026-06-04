using NetSdrMonitor.Core.Abstractions.Persistence;

namespace NetSdrMonitor.Core.Features.Monitoring;

/// <summary>
/// Спільний стан активної сесії: поточне сховище записів і ознака його персистентності.
/// Зв'язує приймач (<see cref="MonitoringService"/>, який створює сховище й пише в нього) та читач
/// (<see cref="RecordFeed"/>, який читає історію) — без прямої залежності одного сервісу від іншого.
/// </summary>
public sealed class RecordSession
{
    private readonly Lock _gate = new();

    private ISignalRecordRepository? _repository;
    private bool _isPersistent;

    /// <summary>
    /// Сховище поточної сесії (null до першого старту).
    /// </summary>
    public ISignalRecordRepository? Repository
    {
        get { lock (_gate) return _repository; }
    }

    /// <summary>
    /// Чи персистентне сховище поточної сесії (файлове SQLite).
    /// </summary>
    public bool IsPersistent
    {
        get { lock (_gate) return _isPersistent; }
    }

    /// <summary>
    /// Прив'язує сесію до нового сховища (виклик приймача на старті).
    /// </summary>
    public void Begin(ISignalRecordRepository repository, bool isPersistent)
    {
        lock (_gate)
        {
            _repository   = repository;
            _isPersistent = isPersistent;
        }
    }
}
