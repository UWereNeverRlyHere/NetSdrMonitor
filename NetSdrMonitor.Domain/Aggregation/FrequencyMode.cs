namespace NetSdrMonitor.Domain.Aggregation;

/// <summary>
/// Режим відображення частоти запису в таблиці.
/// <see cref="First"/> — частота першого сигналу;
/// <see cref="Median"/> — медіана частот усіх сигналів запису (стійка до викидів).
/// </summary>
public enum FrequencyMode : byte
{
    First,
    Median,
}
