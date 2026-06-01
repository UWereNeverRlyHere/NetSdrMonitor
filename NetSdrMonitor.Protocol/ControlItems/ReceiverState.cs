namespace NetSdrMonitor.Protocol;

/// <summary>
/// Бажаний стан приймача для команди Run/Stop. Публічне, «чисте» подання —
/// без байтових значень специфікації (їх знає лише реалізація протоколу).
/// </summary>
public enum ReceiverState
{
    /// <summary>Запустити захоплення й відправлення даних.</summary>
    Running,

    /// <summary>Зупинити захоплення даних.</summary>
    Stopped,
}
