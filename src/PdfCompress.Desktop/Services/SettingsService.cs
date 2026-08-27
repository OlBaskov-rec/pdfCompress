using System.Text.Json;
using PdfCompress.Core.Models;

namespace PdfCompress.Desktop.Services;

/// <summary>
/// Настройки последнего запуска: папки, режим и его параметры. Сохраняются в
/// %AppData%/PdfCompress/settings.json, чтобы при следующем открытии не выбирать всё заново.
/// Повреждённый или отсутствующий файл — не ошибка: берутся значения по умолчанию.
/// </summary>
public sealed class AppSettings
{
    public string? SourceFolder { get; set; }
    public string? OutputFolder { get; set; }
    public bool IncludeSubfolders { get; set; }
    public bool UseLevelMode { get; set; } = true;
    public int Level { get; set; } = (int)CompressionLevel.Medium;
    public double MaxSizeValue { get; set; } = 5;
    public SizeUnit MaxSizeUnit { get; set; } = SizeUnit.Megabytes;
}

public static class SettingsService
{
    private static string FilePath => Path.Combine(AppPaths.DataFolder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            using var stream = File.OpenRead(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(stream) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            AppLog.Error("Не удалось прочитать настройки — берём значения по умолчанию", ex);
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataFolder);
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            AppLog.Error("Не удалось сохранить настройки", ex);
        }
    }
}
