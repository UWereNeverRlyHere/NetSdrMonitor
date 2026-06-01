namespace NetSdrMonitor.Application.Abstractions.Communication;

/// <summary>
/// Стан лінії монітора для відображення в UI. Прив'язки до транспорту немає — це погляд оркестратора.
/// </summary>
public enum ConnectionStatus
{
   /// <summary>
   /// З'єднання немає (ще не стартували або зупинено).
   /// </summary>
   Disconnected,

   /// <summary>
   /// Триває перша спроба підключення.
   /// </summary>
   Connecting,

   /// <summary>
   /// З'єднано, потік даних активний.
   /// </summary>
   Connected,

   /// <summary>
   /// Зв'язок втрачено (обрив/простій), триває відновлення.
   /// </summary>
   Reconnecting,
}
