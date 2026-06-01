namespace NetSdrMonitor.Protocol.Messages;

/// <summary>
/// 3-бітне поле типу повідомлення із заголовка NetSDR (розділ 3 специфікації).
/// Значення збігається з бітами поля; зміст залежить від напрямку (host ↔ target).
/// Для нашого сценарію сервер шле Target Data Item 0 (0b100 = 4).
/// </summary>
public enum SdrMessageType : byte
{
   /// <summary>
   /// Host → Target: встановити Control Item. Target → Host: відповідь на Set/Request.
   /// </summary>
   SetControlItem = 0,

   /// <summary>
   /// Host → Target: запит поточного Control Item. Target → Host: unsolicited Control Item.
   /// </summary>
   RequestControlItem = 1,

   /// <summary>
   /// Host → Target: запит діапазону Control Item. Target → Host: відповідь на діапазон.
   /// </summary>
   RequestControlItemRange = 2,

   /// <summary>
   /// ACK для Data Item (в обидві сторони).
   /// </summary>
   DataItemAck = 3,

   /// <summary>
   /// Data Item канал 0 (наш потік сигналів).
   /// </summary>
   DataItem0 = 4,

   /// <summary>
   /// Data Item канал 1.
   /// </summary>
   DataItem1 = 5,

   /// <summary>
   /// Data Item канал 2.
   /// </summary>
   DataItem2 = 6,

   /// <summary>
   /// Data Item канал 3.
   /// </summary>
   DataItem3 = 7,
}
