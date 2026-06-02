using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetSdrMonitor.Desktop.Settings;

/// <summary>
/// Читає й зберігає налаштування у JSON поруч із застосунком (без реєстру й прихованих тек).
/// </summary>
public sealed class JsonSettingsStore
{
   private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "settings.json");

   // enum-и пишемо рядком ("System"/"Light"/"Dark") — файл лишається читабельним для людини
   private static readonly JsonSerializerOptions Options = new()
   {
      WriteIndented = true,
      Converters    = { new JsonStringEnumConverter() },
   };

   public AppSettings Load()
   {
      try
      {
         if (File.Exists(FilePath))
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
      }
      catch
      {
         // пошкоджений/несумісний файл — стартуємо з дефолтів
      }

      return new AppSettings();
   }

   public void Save(AppSettings settings)
   {
      try
      {
         File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
      }
      catch
      {
         // не вдалося зберегти — не критично для роботи
      }
   }
}
