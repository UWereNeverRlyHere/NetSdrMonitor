using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NetSdrMonitor.Desktop.Features.Console;

/// <summary>
/// Модель консолі логів: батчами (за таймером, на UI-потоці) забирає записи зі сховища-черги
/// й тримає обмежений «хвіст» останніх рядків — щоб консоль не росла безмежно й не топила UI.
/// </summary>
public sealed partial class ConsoleViewModel : ObservableObject
{
   private const int MaxEntries     = 1_000; // скільки рядків тримаємо у видимому хвості
   private const int MaxDrainPerTick = 2_000; // стеля розбору за один тік — захист від сплеску

   private readonly UiLogSink _sink;
   private readonly DispatcherTimer _pump;

   public ConsoleViewModel(UiLogSink sink)
   {
      _sink   = sink;
      Entries = [];

      _pump = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
      _pump.Tick += (_, _) => Drain();
      _pump.Start();
   }

   /// <summary>
   /// Видимі рядки консолі (останні <see cref="MaxEntries"/>).
   /// </summary>
   public ObservableCollection<LogEntry> Entries { get; }

   [RelayCommand]
   private void Clear() => Entries.Clear();

   private void Drain()
   {
      int processed = 0;
      while (processed < MaxDrainPerTick && _sink.TryDequeue(out LogEntry entry))
      {
         Entries.Add(entry);
         processed++;
      }

      // тримаємо лише хвіст: прибираємо найстаріші, якщо вийшли за межу
      int overflow = Entries.Count - MaxEntries;
      for (int i = 0; i < overflow; i++)
         Entries.RemoveAt(0);
   }
}
