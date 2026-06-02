using System.Windows;
using System.Windows.Controls;
using NetSdrMonitor.Desktop.Settings;

namespace NetSdrMonitor.Desktop.Behaviors;

/// <summary>
/// Збереження й відновлення розкладки колонок гріда (порядок + ширина) між запусками.
/// Колонки позначаються стабільним ключем через приєднану властивість <c>Key</c>, тож порядок
/// відновлюється коректно навіть після перетягування мишею.
/// </summary>
public static class ColumnLayout
{
   /// <summary>
   /// Стабільний ключ колонки для зіставлення зі збереженими налаштуваннями (не залежить від позиції).
   /// </summary>
   public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
      "Key", typeof(string), typeof(ColumnLayout), new PropertyMetadata(null));

   public static void SetKey(DependencyObject element, string value) => element.SetValue(KeyProperty, value);

   public static string? GetKey(DependencyObject element) => (string?)element.GetValue(KeyProperty);

   /// <summary>
   /// Знімає поточну розкладку гріда: для кожної підписаної колонки — її ключ, позицію та ширину.
   /// </summary>
   public static IReadOnlyList<ColumnSetting> Capture(DataGrid grid)
   {
      var result = new List<ColumnSetting>(grid.Columns.Count);
      foreach (DataGridColumn column in grid.Columns)
      {
         if (GetKey(column) is not { } key)
            continue;

         result.Add(new ColumnSetting
         {
            Key   = key,
            Order = column.DisplayIndex,
            Width = column.ActualWidth,
         });
      }

      return result;
   }

   /// <summary>
   /// Застосовує збережену розкладку: спершу ширини, потім позиції в збереженому порядку.
   /// Невідомі ключі ігноруються, тож додавання нової колонки не ламає старі налаштування.
   /// </summary>
   public static void Apply(DataGrid grid, IReadOnlyList<ColumnSetting> settings)
   {
      if (settings.Count == 0)
         return;

      Dictionary<string, ColumnSetting> byKey = settings
         .GroupBy(s => s.Key)
         .ToDictionary(g => g.Key, g => g.Last());

      // ширини виставляємо незалежно від порядку
      foreach (DataGridColumn column in grid.Columns)
         if (GetKey(column) is { } key && byKey.TryGetValue(key, out ColumnSetting? setting) && setting.Width > 0)
            column.Width = new DataGridLength(setting.Width);

      // позиції: впорядковуємо відомі колонки за збереженим Order і призначаємо DisplayIndex по черзі.
      // Призначення у зростаючому цільовому порядку коректне — WPF сам зсуває решту колонок.
      List<DataGridColumn> ordered = grid.Columns
         .Where(c => GetKey(c) is { } key && byKey.ContainsKey(key))
         .OrderBy(c => byKey[GetKey(c)!].Order)
         .ToList();

      for (int i = 0; i < ordered.Count; i++)
      {
         try
         {
            ordered[i].DisplayIndex = i;
         }
         catch (ArgumentOutOfRangeException)
         {
            // про всяк випадок: некоректний збережений індекс не має валити застосунок
         }
      }
   }
}
