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
                Converters = { new JsonStringEnumConverter() },
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
            // пишемо в тимчасовий файл і атомарно підміняємо: обірваний запис (вихід через трей,
            // стоп відладчика) не псує наявний settings.json і не скидає всі настройки в дефолт
            string tempPath = FilePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, Options));
            File.Move(tempPath, FilePath, overwrite: true);
        }
        catch
        {
            // не вдалося зберегти — не критично для роботи
        }
    }
}
