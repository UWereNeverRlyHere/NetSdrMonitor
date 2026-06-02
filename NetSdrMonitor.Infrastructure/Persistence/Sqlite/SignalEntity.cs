namespace NetSdrMonitor.Infrastructure.Persistence.Sqlite;

/// <summary>
/// Рядок таблиці сигналів — одна детекція в межах запису. Модель зберігання:
/// поля віддзеркалюють доменний <c>Signal</c>, а <see cref="Ordinal"/> фіксує порядок у записі.
/// </summary>
public sealed class SignalEntity
{
   /// <summary>
   /// Первинний ключ (автоінкремент).
   /// </summary>
   public long Id { get; set; }

   /// <summary>
   /// Зовнішній ключ на запис-власник.
   /// </summary>
   public long RecordId { get; set; }

   /// <summary>
   /// Позиція сигналу в записі — щоб відновити вихідну послідовність.
   /// </summary>
   public int Ordinal { get; set; }

   /// <summary>
   /// Час детекції у мілісекундах Unix epoch (UTC).
   /// </summary>
   public long TimestampUnixMs { get; set; }

   /// <summary>
   /// Центральна частота сигналу в герцах.
   /// </summary>
   public ulong FrequencyHz { get; set; }

   /// <summary>
   /// Ширина смуги сигналу в герцах.
   /// </summary>
   public uint BandwidthHz { get; set; }

   /// <summary>
   /// Відношення сигнал/шум у децибелах.
   /// </summary>
   public double SnrDb { get; set; }
}
