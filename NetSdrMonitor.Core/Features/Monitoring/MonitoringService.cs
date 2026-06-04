using NetSdrMonitor.Core.Abstractions.Communication;
using NetSdrMonitor.Core.Abstractions.Persistence;
using NetSdrMonitor.Domain.Aggregation;
using NetSdrMonitor.Domain.Signals;

namespace NetSdrMonitor.Core.Features.Monitoring;

/// <summary>
/// Прикладний сервіс приймання: володіє життєвим циклом сесії (Start/Stop/Clear), на фоні тягне потік
/// сигналів монітора крізь доменний агрегатор, осідає закриті записи у сховище й рахує прийняте.
/// Назовні віддає UI-агностичні події — зміну стану лінії, зміни живих записів і сигнал «перечитати набір».
/// Читання історії — окремий <see cref="RecordFeed"/> над тим самим <see cref="RecordSession"/>.
/// </summary>
public sealed class MonitoringService(
    ISdrMonitorFactory   monitorFactory,
    ISessionStoreFactory storeFactory,
    RecordSession        session) : IAsyncDisposable
{
    private ISdrMonitor? _monitor;
    private SignalAggregator? _aggregator;
    private CancellationTokenSource? _drainCts;
    private Task _drainTask = Task.CompletedTask;
    private Task _persistChain = Task.CompletedTask; // ланцюг записів у сховище — точка очікування «усе осіло»
    private long _received; // пишеться з фонового drain, читається UI

    /// <summary>Чи триває сесія приймання.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Останній стан лінії.</summary>
    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;

    /// <summary>Чи персистентне сховище поточної сесії (впливає на читання діапазону дат).</summary>
    public bool IsPersistentStore => session.IsPersistent;

    /// <summary>Скільки сигналів прийнято за поточну сесію.</summary>
    public long ReceivedCount => Interlocked.Read(ref _received);

    /// <summary>Зміна стану лінії. Може здійматися з фонового потоку — підписник маршалить сам.</summary>
    public event Action<ConnectionStatus>? StatusChanged;

    /// <summary>Зміна живого запису (Opened/Updated/Closed). Здіймається з фонового drain-потоку.</summary>
    public event Action<RecordChange>? RecordChanged;

    /// <summary>Набір записів змінився повністю (старт сесії / очистка) — UI має перечитати дані.</summary>
    public event Action? SourceChanged;

    /// <summary>
    /// Починає сесію: готує сховище під поточні настройки, відкриває агрегатор і запускає монітор.
    /// </summary>
    public async Task StartAsync()
    {
        if (IsRunning)
            return;

        // ранній страж від повторного старту: UI встигає натиснути «старт» двічі, доки триває await нижче
        IsRunning = true;
        try
        {
            // сховище сесії під поточні настройки; обидва сервіси бачать його через RecordSession
            SessionStore store = await storeFactory.CreateAsync();
            session.Begin(store.Repository, store.IsPersistent);

            _aggregator = SignalAggregator.Create()
                .OnRecordOpened(record => RecordChanged?.Invoke(new RecordChange { Record = record, Kind = RecordChangeKind.Opened }))
                .OnSignalAppended((record, _) => RecordChanged?.Invoke(new RecordChange { Record = record, Kind = RecordChangeKind.Updated }))
                .OnRecordClosed((record, _) =>
                {
                    RecordChanged?.Invoke(new RecordChange { Record = record, Kind = RecordChangeKind.Closed });
                    _persistChain = ChainPersistAsync(_persistChain, record); // закритий запис осідає у сховище
                })
                .Build();

            _persistChain = Task.CompletedTask;

            _monitor = monitorFactory.Create();
            _monitor.StatusChanged += OnMonitorStatus;

            Interlocked.Exchange(ref _received, 0);
            _drainCts = new CancellationTokenSource();
            _drainTask = Task.Run(() => DrainAsync(_monitor, _drainCts.Token));

            _monitor.Start();
            SourceChanged?.Invoke(); // UI вантажить стартовий «хвіст» (або діапазон, якщо фільтр уже стоїть)
        }
        catch
        {
            IsRunning = false; // старт не вдався — знімаємо прапор, щоб можна було спробувати знову
            throw;
        }
    }

    /// <summary>
    /// Зупиняє сесію: гасить приймання, закриває останній запис і чекає, доки все осяде у сховищі.
    /// </summary>
    public async Task StopAsync()
    {
        if (!IsRunning)
            return;

        IsRunning = false;

        if (_drainCts is not null)
            await _drainCts.CancelAsync(); // обриваємо приймання: drain виходить як Cancelled

        try
        {
            await _drainTask;
        }
        catch
        {
            // drain сам гасить свої винятки; тут лише чекаємо виходу
        }

        if (_monitor is not null)
        {
            _monitor.StatusChanged -= OnMonitorStatus;
            await _monitor.DisposeAsync(); // гасить монітор і мок-сервер під ним
            _monitor = null;
        }

        _aggregator?.Dispose(); // закриває останній відкритий запис -> Closed + персист
        _aggregator = null;
        await _persistChain; // дочекатися, доки всі закриті записи осядуть у сховищі

        _drainCts?.Dispose();
        _drainCts = null;
        Status = ConnectionStatus.Stopped;
    }

    /// <summary>
    /// Очищає сховище поточної сесії й обнуляє лічильник; повідомляє UI перечитати (порожній) набір.
    /// </summary>
    public async Task ClearAsync()
    {
        if (session.Repository is { } repository)
            await repository.ClearAsync();

        Interlocked.Exchange(ref _received, 0);
        SourceChanged?.Invoke();
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    // фоновий приймач: рахує сигнали й проганяє їх крізь агрегатор (події летять підписникам синхронно)
    private async Task DrainAsync(ISdrMonitor monitor, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (Signal signal in monitor.Signals(cancellationToken))
            {
                Interlocked.Increment(ref _received);
                _aggregator?.Process(signal);
            }
        }
        catch (OperationCanceledException)
        {
            // штатна зупинка
        }
    }

    private void OnMonitorStatus(object? sender, ConnectionStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(status);
    }

    private async Task ChainPersistAsync(Task previous, SignalRecord record)
    {
        await previous; // черга дає одну точку очікування «усі записи завершені»
        await PersistAsync(record);
    }

    private async Task PersistAsync(SignalRecord record)
    {
        try
        {
            if (session.Repository is { } repository)
                await repository.AddAsync(record);
        }
        catch
        {
            // збій сховища не має валити приймання сигналів
        }
    }
}
