using NetSdrMonitor.Domain.Signals;
using NetSdrMonitor.Protocol.Messages;

namespace NetSdrMonitor.Protocol;

/// <summary>
/// Парсер NetSDR: маппінг між готовим <see cref="SdrMessage"/> і доменним <see cref="Signal"/>.
/// Робить рівно дві речі — збирає сигнал із повідомлення та повідомлення із сигналу.
/// Байтів потоку, буферів і позицій не торкається (цим керує протокол + оркестратор).
/// </summary>
public interface ISdrMessageParser
{
    /// <summary>
    /// Намагається витлумачити повідомлення як сигнал (Data Item 0).
    /// false — якщо це не сигнал (інший тип чи некоректне тіло) — очікувана гілка, не виняток.
    /// </summary>
    bool TryToSignal(SdrMessage message, out Signal signal);

    /// <summary>Збирає повідомлення Data Item 0 із сигналу (бік сервера).</summary>
    SdrMessage FromSignal(Signal signal);
}
