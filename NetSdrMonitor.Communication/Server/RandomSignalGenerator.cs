using NetSdrMonitor.Domain.Signals;

namespace NetSdrMonitor.Communication.Server;

/// <summary>
/// Генератор псевдовипадкових сигналів для мок-сервера. Тримається біля поточної «станції»
/// (дрейф у межах смуги), а з імовірністю (1 − шанс тієї ж станції) перестроюється на нову частоту —
/// щоб згортання сигналів у записи мало сенс (кілька детекцій біля однієї частоти + зрідка стрибок).
/// Діапазон станцій і шанс беруться з налаштувань. Сідований — відтворюваний у тестах.
/// </summary>
public sealed class RandomSignalGenerator
{
    private static readonly uint[] Bandwidths = [5_000, 10_000, 25_000, 50_000]; // Гц

    private readonly Random _random;
    private readonly ulong _minCenterHz;
    private readonly ulong _maxCenterHz;
    private readonly double _sameStationProbability; // 0..1: шанс лишитися біля тієї ж станції

    private ulong _centerHz;
    private uint  _bandwidthHz;

    /// <param name="seed">Сід для відтворюваності; null — недетерміновано.</param>
    /// <param name="options">Діапазон станцій і шанс тієї ж станції; null — типові значення.</param>
    public RandomSignalGenerator(int? seed = null, RandomSignalGeneratorOptions? options = null)
    {
        RandomSignalGeneratorOptions effective = options ?? new RandomSignalGeneratorOptions();

        _random                 = seed is { } s ? new Random(s) : new Random();
        _minCenterHz            = effective.MinCenterHz;
        _maxCenterHz            = Math.Max(effective.MaxCenterHz, effective.MinCenterHz); // боронимось від перевернутого діапазону
        _sameStationProbability = Math.Clamp(effective.SameStationProbability, 0.0, 1.0);
        Retune();
    }

    /// <summary>
    /// Видає наступний сигнал: біля поточної станції або (рідше) уже з новою частотою.
    /// </summary>
    public Signal Next()
    {
        // з імовірністю (1 − шанс тієї ж станції) перестроюємось на нову станцію (тобто відкриваємо новий запис)
        if (_random.NextDouble() >= _sameStationProbability)
            Retune();

        // джитер у межах смуги => сигнал має шанс впасти в той самий запис, що й попередні
        long half = _bandwidthHz / 2;
        long jitter = half > 0 ? _random.NextInt64(-half, half) : 0;
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
        _centerHz = _maxCenterHz > _minCenterHz
            ? (ulong)_random.NextInt64((long)_minCenterHz, (long)_maxCenterHz)
            : _minCenterHz;
        _bandwidthHz = Bandwidths[_random.Next(Bandwidths.Length)];
    }
}
