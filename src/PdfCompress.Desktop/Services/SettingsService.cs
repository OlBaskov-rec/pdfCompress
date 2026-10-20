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
    /// <summary>Папки-источники. Список, а не одна папка, начиная с версии 0.2.</summary>
    public List<string>? SourceFolders { get; set; }

    /// <summary>
    /// Единственная папка-источник из настроек прежних версий. Только для чтения при переходе —
    /// новые настройки пишутся в <see cref="SourceFolders"/>.
    /// </summary>
    public string? SourceFolder { get; set; }

    public string? OutputFolder { get; set; }

    /// <summary>К чему применяется предельный размер: к файлу, к папке или к каждой подпапке.</summary>
    public TargetScope Scope { get; set; } = TargetScope.File;

    /// <summary>
    /// Папку результатов выбрал сам пользователь. Если нет — при смене исходной папки
    /// предлагается вложенная папка по умолчанию.
    /// </summary>
    public bool OutputFolderIsCustom { get; set; }
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
