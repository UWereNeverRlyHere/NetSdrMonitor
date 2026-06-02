using CommunityToolkit.Mvvm.ComponentModel;
using NetSdrMonitor.Core.Abstractions.Communication;

namespace NetSdrMonitor.Desktop.Features.Tray;

/// <summary>
/// Рядок статусу лінії у меню трея: «{порт}: {стан}». Оновлюється наживо при зміні Status.
/// </summary>
public sealed partial class TrayStatusLine(int lineId, ConnectionStatus status) : ObservableObject
{
   [ObservableProperty]
   [NotifyPropertyChangedFor(nameof(Header))]
   private ConnectionStatus _status = status;

   public int LineId { get; } = lineId;

   public string Header => $"{LineId}: {Describe(Status)}";

   private static string Describe(ConnectionStatus status) => status switch
   {
         ConnectionStatus.Disconnected => "відключено",
         ConnectionStatus.Connecting   => "підключення…",
         ConnectionStatus.Connected    => "підключено",
         ConnectionStatus.Reconnecting => "відновлення…",
         ConnectionStatus.Stopped      => "зупинено",
         _                             => status.ToString(),
   };
}
