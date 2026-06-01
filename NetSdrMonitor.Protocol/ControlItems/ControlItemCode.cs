namespace NetSdrMonitor.Protocol.ControlItems;

/// <summary>
/// 16-бітні коди Control Item з NetSDR (розділ 4 специфікації).
/// Перелік повний для повноти картини протоколу; поведінку реалізуємо лише для Run/Stop
/// (<see cref="ReceiverState"/>), решта — апаратні налаштування.
/// </summary>
public enum ControlItemCode : ushort
{
    // 4.1 — загальні
    TargetName           = 0x0001,
    TargetSerialNumber   = 0x0002,
    InterfaceVersion     = 0x0003,
    HardwareFirmwareVer  = 0x0004,
    StatusErrorCode      = 0x0005,
    ProductId            = 0x0009,
    Options              = 0x000A,
    SecurityCode         = 0x000B,
    FpgaConfiguration    = 0x000C,

    // 4.2 — приймач
    ReceiverState        = 0x0018, // Run/Stop — єдине, що реалізуємо
    ReceiverChannelSetup = 0x0019,
    ReceiverFrequency    = 0x0020,
    NcoPhaseOffset       = 0x0022,
    RfGain               = 0x0038,
    DownConverterGain    = 0x003A,
    RfFilter             = 0x0044,
    AdModes              = 0x008A,

    // 4.4 — вихід даних
    IqOutputSampleRate   = 0x00B8,
    DataOutputPacketSize = 0x00C4,
    DataOutputUdpAddress = 0x00C5,
}
