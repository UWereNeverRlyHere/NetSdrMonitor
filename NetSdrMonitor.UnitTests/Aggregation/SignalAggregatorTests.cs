using NetSdrMonitor.Communication.Server;
using NetSdrMonitor.Domain.Aggregation;
using NetSdrMonitor.Domain.Signals;

namespace NetSdrMonitor.UnitTests.Aggregation;

/// <summary>
/// Тести оркестратора потоку детекцій: відкриття / оновлення / закриття записів,
/// межі діапазону поглинання, завершення потоку та захист після нього.
/// Агрегатор збираємо через <see cref="SignalAggregatorBuilder"/>; сигнали будуємо вручну
/// (детерміновані межі) або через <see cref="RandomSignalGenerator"/> (потокові інваріанти).
/// </summary>
public sealed class SignalAggregatorTests
{
    // станція для детермінованих тестів: смуга 10 кГц => half = 5 кГц
    private const ulong CenterHz        = 100_000_000;
    private const uint  BandwidthHz     = 10_000;
    private const ulong LowInclusiveHz  = 99_995_000;  // f − bw/2 (включно)
    private const ulong HighExclusiveHz = 100_005_000; // f + bw/2 (виключно)

    // --- Відкриття запису ---

    [Fact]
    public void Process_FirstSignal_OpensRecordOnly()
    {
        var events = new Recorder();
        using SignalAggregator aggregator = events.Build();

        aggregator.Process(SignalAt(CenterHz));

        // перший сигнал лише відкриває запис: без поглинання та без закриття
        Assert.Single(events.Opened);
        Assert.Empty(events.Appended);
        Assert.Empty(events.Closed);
        Assert.Same(events.Opened[0], aggregator.Current);
        Assert.Equal(CenterHz, aggregator.Current!.FrequencyHz);
        Assert.Equal(1, aggregator.Current.Count);
    }

    // --- Поглинання в межах смуги ---

    [Fact]
    public void Process_SignalWithinBand_AppendsToSameRecord()
    {
        var events = new Recorder();
        using SignalAggregator aggregator = events.Build();

        aggregator.Process(SignalAt(CenterHz));
        Signal within = SignalAt(CenterHz + 1_000);
        aggregator.Process(within);

        // другий сигнал у смузі: той самий рядок, Count++, без нового відкриття/закриття
        Assert.Single(events.Opened);
        Assert.Empty(events.Closed);
        Assert.Single(events.Appended);
        Assert.Same(events.Opened[0], events.Appended[0].Record);
        Assert.Equal(within, events.Appended[0].Signal);
        Assert.Equal(2, aggregator.Current!.Count);
    }

    [Fact]
    public void Process_SignalAtLowerBoundary_IsAppended()
    {
        var events = new Recorder();
        using SignalAggregator aggregator = events.Build();

        aggregator.Process(SignalAt(CenterHz));
        aggregator.Process(SignalAt(LowInclusiveHz)); // нижня межа — включно

        Assert.Empty(events.Closed);
        Assert.Single(events.Appended);
        Assert.Equal(2, aggregator.Current!.Count);
    }

    [Fact]
    public void Process_SignalJustBelowUpperBoundary_IsAppended()
    {
        var events = new Recorder();
        using SignalAggregator aggregator = events.Build();

        aggregator.Process(SignalAt(CenterHz));
        aggregator.Process(SignalAt(HighExclusiveHz - 1)); // одразу під верхньою межею

        Assert.Empty(events.Closed);
        Assert.Single(events.Appended);
        Assert.Equal(2, aggregator.Current!.Count);
    }

    // --- Межі діапазону: витіснення ---

    [Fact]
    public void Process_SignalAtUpperBoundary_OpensNewRecord()
    {
        var events = new Recorder();
        using SignalAggregator aggregator = events.Build();

        aggregator.Process(SignalAt(CenterHz));
        aggregator.Process(SignalAt(HighExclusiveHz)); // верхня межа — виключно => новий запис

        Assert.Equal(2, events.Opened.Count);
        Assert.Single(events.Closed);
        Assert.Equal(RecordCloseReason.OutOfRange, events.Closed[0].Reason);
        Assert.Equal(HighExclusiveHz, aggregator.Current!.FrequencyHz);
        Assert.Equal(1, aggregator.Current.Count);
    }

    [Fact]
    public void Process_SignalBelowLowerBoundary_OpensNewRecord()
    {
        var events = new Recorder();
        using SignalAggregator aggregator = events.Build();

        aggregator.Process(SignalAt(CenterHz));
        aggregator.Process(SignalAt(LowInclusiveHz - 1)); // нижче нижньої межі => новий запис

        Assert.Equal(2, events.Opened.Count);
        Assert.Single(events.Closed);
        Assert.Equal(RecordCloseReason.OutOfRange, events.Closed[0].Reason);
    }

    [Fact]
    public void Process_OutOfRangeSignal_ClosesPreviousThenOpensNew()
    {
        var events = new Recorder();
        using SignalAggregator aggregator = events.Build();

        Signal first = SignalAt(CenterHz);
        Signal far   = SignalAt(CenterHz + 50_000_000); // явно поза смугою
        aggregator.Process(first);
        aggregator.Process(far);

        // порядок подій: спершу закриваємо попередній запис, потім відкриваємо новий
        Assert.Equal(
            new[] { AggEvent.Opened, AggEvent.Closed, AggEvent.Opened },
            events.Sequence);

        // закрито саме попередній запис (із першим сигналом), причина — витіснення
        Assert.Equal(first.FrequencyHz, events.Closed[0].Record.FrequencyHz);
        Assert.Equal(RecordCloseReason.OutOfRange, events.Closed[0].Reason);

        // новий поточний запис стоїть на сигналі, що випав із діапазону
        Assert.Same(events.Opened[1], aggregator.Current);
        Assert.Equal(far.FrequencyHz, aggregator.Current!.FrequencyHz);
    }

    // --- Вміст агрегованого запису ---

    [Fact]
    public void Process_AppendsWithinBand_RecordTracksCountAndMedian()
    {
        var events = new Recorder();
        using SignalAggregator aggregator = events.Build();

        // три сигнали в одній смузі з відомою медіаною частот
        aggregator.Process(SignalAt(CenterHz));     // 100_000_000
        aggregator.Process(SignalAt(CenterHz + 2)); // 100_000_002
        aggregator.Process(SignalAt(CenterHz + 4)); // 100_000_004

        SignalRecord record = aggregator.Current!;
        Assert.Equal(3, record.Count);
        Assert.Equal(CenterHz + 2, record.MedianFrequencyHz); // медіана трьох значень — середнє
        Assert.Equal(CenterHz,     record.DisplayFrequencyHz(FrequencyMode.First));
        Assert.Equal(CenterHz + 2, record.DisplayFrequencyHz(FrequencyMode.Median));
    }

    // --- Завершення потоку ---

    [Fact]
    public void Complete_ClosesLastRecordAsStreamCompleted_FiresCompletedOnce_CurrentNull()
    {
        var events = new Recorder();
        SignalAggregator aggregator = events.Build();

        aggregator.Process(SignalAt(CenterHz));
        aggregator.Complete();

        Assert.Single(events.Closed);
        Assert.Equal(RecordCloseReason.StreamCompleted, events.Closed[0].Reason);
        Assert.Equal(1, events.CompletedCount);
        Assert.Null(aggregator.Current);
    }

    [Fact]
    public void Complete_CalledTwice_IsIdempotent()
    {
        var events = new Recorder();
        SignalAggregator aggregator = events.Build();

        aggregator.Process(SignalAt(CenterHz));
        aggregator.Complete();
        aggregator.Complete(); // повторне завершення — без подвійних подій

        Assert.Single(events.Closed);
        Assert.Equal(1, events.CompletedCount);
    }

    [Fact]
    public void Complete_WithoutSignals_FiresCompletedWithoutClosing()
    {
        var events = new Recorder();
        SignalAggregator aggregator = events.Build();

        aggregator.Complete(); // жодного сигналу не надходило

        Assert.Empty(events.Closed);
        Assert.Equal(1, events.CompletedCount);
    }

    [Fact]
    public void Dispose_CompletesStreamLikeComplete()
    {
        var events = new Recorder();

        using (SignalAggregator aggregator = events.Build())
            aggregator.Process(SignalAt(CenterHz));

        // вихід із using => Dispose => закриття останнього запису + OnCompleted
        Assert.Single(events.Closed);
        Assert.Equal(RecordCloseReason.StreamCompleted, events.Closed[0].Reason);
        Assert.Equal(1, events.CompletedCount);
    }

    // --- Захист після завершення ---

    [Fact]
    public void Process_AfterComplete_ThrowsObjectDisposed()
    {
        var events = new Recorder();
        SignalAggregator aggregator = events.Build();
        aggregator.Process(SignalAt(CenterHz));
        aggregator.Complete();

        Assert.Throws<ObjectDisposedException>(() => aggregator.Process(SignalAt(CenterHz)));
    }

    [Fact]
    public void Process_AfterDispose_ThrowsObjectDisposed()
    {
        var events = new Recorder();
        SignalAggregator aggregator = events.Build();
        aggregator.Process(SignalAt(CenterHz));
        aggregator.Dispose();

        Assert.Throws<ObjectDisposedException>(() => aggregator.Process(SignalAt(CenterHz)));
    }

    // --- Робота без колбеків ---

    [Fact]
    public void Process_WithoutCallbacks_TracksCurrentAndDoesNotThrow()
    {
        // жоден обробник не під'єднаний — агрегатор має лишатися повністю робочим
        using SignalAggregator aggregator = SignalAggregator.Create().Build();

        aggregator.Process(SignalAt(CenterHz));
        Assert.Equal(CenterHz, aggregator.Current!.FrequencyHz);

        aggregator.Process(SignalAt(CenterHz + 1_000));      // у смузі
        Assert.Equal(2, aggregator.Current!.Count);

        aggregator.Process(SignalAt(CenterHz + 50_000_000)); // поза смугою => новий запис
        Assert.Equal(1, aggregator.Current!.Count);
        Assert.Equal(CenterHz + 50_000_000, aggregator.Current.FrequencyHz);
    }

    // --- Сценарії потоку ---

    [Fact]
    public void Process_MultipleStations_OneRecordPerStation_WithCorrectReasons()
    {
        var events = new Recorder();
        SignalAggregator aggregator = events.Build();

        // три «станції» по своїх частотах; усередині кожної — джитер у межах смуги
        Signal[] stream =
        [
            SignalAt(10_000_000), SignalAt(10_000_001), SignalAt(9_999_999), // станція A (3 детекції)
            SignalAt(20_000_000), SignalAt(20_000_002),                      // станція B (2 детекції)
            SignalAt(30_000_000),                                            // станція C (1 детекція)
        ];
        foreach (Signal signal in stream)
            aggregator.Process(signal);
        aggregator.Complete();

        // три станції => три записи; перші дві витіснено, останню закрито завершенням потоку
        Assert.Equal(3, events.Opened.Count);
        Assert.Equal(
            new[] { RecordCloseReason.OutOfRange, RecordCloseReason.OutOfRange, RecordCloseReason.StreamCompleted },
            events.Closed.Select(c => c.Reason));
        Assert.Equal(new[] { 3, 2, 1 }, events.Closed.Select(c => c.Record.Count));
        Assert.Equal(
            new ulong[] { 10_000_000, 20_000_000, 30_000_000 },
            events.Closed.Select(c => c.Record.FrequencyHz));
        Assert.Equal(1, events.CompletedCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(2026)]
    public void Process_RandomGeneratorStream_RecordsStayConsistent(int seed)
    {
        var generator = new RandomSignalGenerator(seed);
        var events     = new Recorder();
        const int total = 400;

        using (SignalAggregator aggregator = events.Build())
        {
            for (int i = 0; i < total; i++)
                aggregator.Process(generator.Next());
        }

        // кожен відкритий запис зрештою закрито; останній — через завершення потоку
        Assert.Equal(events.Opened.Count, events.Closed.Count);
        Assert.Equal(1, events.CompletedCount);
        Assert.Equal(RecordCloseReason.StreamCompleted, events.Closed[^1].Reason);
        Assert.All(
            events.Closed.Take(events.Closed.Count - 1),
            c => Assert.Equal(RecordCloseReason.OutOfRange, c.Reason));

        // жодної детекції не загублено: сума Count усіх записів дорівнює числу поданих сигналів
        Assert.Equal(total, events.Closed.Sum(c => c.Record.Count));

        // інваріант діапазону: усі сигнали запису лежать у смузі його першого сигналу
        foreach ((SignalRecord record, _) in events.Closed)
            Assert.All(record.Signals, s => Assert.True(record.Accepts(s)));
    }

    // --- Допоміжне ---

    /// <summary>
    /// Будує сигнал із заданою частотою; решта полів — типові (смуга 10 кГц).
    /// </summary>
    private static Signal SignalAt(ulong frequencyHz, uint bandwidthHz = BandwidthHz) =>
        new()
        {
            TimestampUnixMs = 0,
            FrequencyHz     = frequencyHz,
            BandwidthHz     = bandwidthHz,
            SnrDb           = 12.0,
        };

    /// <summary>
    /// Подія агрегатора — щоб тести могли перевіряти не лише факт, а й порядок викликів колбеків.
    /// </summary>
    private enum AggEvent : byte
    {
        Opened,
        Appended,
        Closed,
        Completed,
    }

    /// <summary>
    /// Записувач подій: під'єднується до білдера й накопичує всі виклики колбеків
    /// (з деталями та спільним упорядкованим журналом), щоб тест читав готовий результат.
    /// </summary>
    private sealed class Recorder
    {
        public List<AggEvent>                                       Sequence       { get; } = [];
        public List<SignalRecord>                                   Opened         { get; } = [];
        public List<(SignalRecord Record, Signal Signal)>           Appended       { get; } = [];
        public List<(SignalRecord Record, RecordCloseReason Reason)> Closed        { get; } = [];
        public int                                                  CompletedCount { get; private set; }

        /// <summary>
        /// Збирає агрегатор, у якому кожен колбек пише у відповідний журнал.
        /// </summary>
        public SignalAggregator Build() =>
            SignalAggregator.Create()
                .OnRecordOpened(record =>
                {
                    Opened.Add(record);
                    Sequence.Add(AggEvent.Opened);
                })
                .OnSignalAppended((record, signal) =>
                {
                    Appended.Add((record, signal));
                    Sequence.Add(AggEvent.Appended);
                })
                .OnRecordClosed((record, reason) =>
                {
                    Closed.Add((record, reason));
                    Sequence.Add(AggEvent.Closed);
                })
                .OnCompleted(() =>
                {
                    CompletedCount++;
                    Sequence.Add(AggEvent.Completed);
                })
                .Build();
    }
}
