using NetSdrMonitor.Domain.Signals;

namespace NetSdrMonitor.Communication.Server;

/// <summary>
/// Генератор псевдовипадкових сигналів для мок-сервера. Тримається біля поточної «станції»
/// (дрейф у межах смуги), час від часу перестроюється на нову частоту — щоб згортання сигналів
/// у записи мало сенс (кілька детекцій біля однієї частоти + зрідка стрибок). Сідований —
/// відтворюваний у тестах.
/// </summary>
public sealed class RandomSignalGenerator
{
    private const ulong MinCenterHz = 1_000_000;   // 1 МГц
    private const ulong MaxCenterHz = 150_000_000;  // 150 МГц

    private static readonly uint[] Bandwidths = [5_000, 10_000, 25_000, 50_000]; // Гц

    private readonly Random _random;
    private readonly double _retuneProbability;

    private ulong _centerHz;
    private uint  _bandwidthHz;

    /// <param name="seed">Сід для відтворюваності; null — недетерміновано.</param>
    /// <param name="sameRecordChancePercent">
    /// Шанс у відсотках (0..100), що наступний сигнал лишиться біля тієї самої станції й потрапить
    /// у той самий запис. Перестроювання на нову частоту відбувається з доповнюючою ймовірністю.
    /// </param>
    public RandomSignalGenerator(int? seed = null, int sameRecordChancePercent = 90)
    {
        _random = seed is { } s ? new Random(s) : new Random();
        // відсоток «лишитись» -> ймовірність перестроїтись (тобто відкрити новий запис)
        _retuneProbability = 1.0 - Math.Clamp(sameRecordChancePercent, 0, 100) / 100.0;
        Retune();
    }

    /// <summary>
    /// Видає наступний сигнал: біля поточної станції або (зрідка) уже з новою частотою.
    /// </summary>
    public Signal Next()
    {
        if (_random.NextDouble() < _retuneProbability)
            Retune();

        // джитер у межах смуги => сигнал має шанс впасти в той самий запис, що й попередні
        long half = _bandwidthHz / 2;
        long jitter = _random.NextInt64(-half, half);
        var frequencyHz = (ulong)((long)_centerHz + jitter);

        return new Signal
        {
            TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            FrequencyHz     = frequencyHz,
            BandwidthHz     = _bandwidthHz,
            SnrDb           = Math.Round(5.0 + _random.NextDouble() * 35.0, 1), // 5..40 дБ
        };
    }

    private void Retune()
    {
        _centerHz    = (ulong)_random.NextInt64((long)MinCenterHz, (long)MaxCenterHz);
        _bandwidthHz = Bandwidths[_random.Next(Bandwidths.Length)];
    }
}
