namespace NetSdrMonitor.Protocol.Messages;

/// <summary>
/// 2-байтовий заголовок повідомлення NetSDR
/// Розкладка (Little Endian): [8 біт Length lsb][3 біт Type][5 біт Length msb].
/// 13-бітна Length — повна довжина повідомлення В БАЙТАХ, включно з цими 2 байтами (0..8191).
/// Спецвипадок Data Item: Length == 0 означає реальні 8194 байти (8192 даних + 2 заголовка).
/// </summary>
public readonly record struct SdrMessageHeader
{
   /// <summary>
   /// Розмір заголовка у байтах.
   /// </summary>
   public const int Size = 2;

   /// <summary>
   /// Реальна довжина для спецвипадку (закодована як Length == 0).
   /// </summary>
   public const int DataItemMaxLength = 8194;
   
   /// <summary>Довжина корисного навантаження (тіла) без 2 байтів заголовка.</summary>
   public int PayloadLength => Length - Size;
   
   /// <summary>Повна довжина повідомлення в байтах, включно з 2 байтами заголовка.</summary>
   public required ushort Length { get; init; }

   /// <summary>Тип повідомлення (3 біти).</summary>
   public required SdrMessageType Type { get; init; }

   /// <summary>
   /// Фабрика з типу та довжини тіла (зручніше, ніж рахувати повну довжину вручну).
   /// </summary>
   public static SdrMessageHeader FromPayload(SdrMessageType type, int payloadLength) => new()
   {
         Type   = type,
         Length = (ushort)(payloadLength + Size)
   };

   /// <summary>Розбирає 2-байтовий заголовок (Little Endian).</summary>
   public static SdrMessageHeader Parse(ReadOnlySpan<byte> twoBytes)
   {
      if (twoBytes.Length < Size)
         throw new ArgumentException("Заголовок потребує щонайменше 2 байти.", nameof(twoBytes));

      int length = twoBytes[0] | ((twoBytes[1] & 0x1F) << 8);
      var type = (SdrMessageType)((twoBytes[1] >> 5) & 0x07);

      if (length == 0)
         length = DataItemMaxLength; // спецвипадок data item

      return new()
      {
            Length = (ushort)length,
            Type   = type
      };
   }

   /// <summary>
   /// Записує заголовок у 2 байти призначення (Little Endian).
   /// </summary>
   public void WriteTo(Span<byte> destination)
   {
      if (destination.Length < Size)
         throw new ArgumentException("Призначення потребує щонайменше 2 байти.", nameof(destination));

      // спецвипадок: 8194 кодуємо назад як 0
      ushort raw = Length == DataItemMaxLength ? (ushort)0 : Length;

      destination[0] = (byte)(raw & 0xFF);
      destination[1] = (byte)(((raw >> 8) & 0x1F) | (((byte)Type & 0x07) << 5));
   }
}
