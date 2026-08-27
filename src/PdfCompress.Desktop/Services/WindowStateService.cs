using System.Text.Json;

namespace PdfCompress.Desktop.Services;

/// <summary>
/// Запоминает размер окна между запусками в %AppData%/PdfCompress/window.json, чтобы выбранный
/// пользователем размер не сбрасывался к значению по умолчанию. Позицию намеренно не храним:
/// центрирование надёжнее на конфигурациях с несколькими мониторами.
/// </summary>
public static class WindowStateService
{
    public sealed class Geometry
    {
        public double Width { get; set; }
        public double Height { get; set; }
    }

    private static string FilePath => Path.Combine(AppPaths.DataFolder, "window.json");

    public static Geometry? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            using var stream = File.OpenRead(FilePath);
            var g = JsonSerializer.Deserialize<Geometry>(stream);
            return g is { Width: > 0, Height: > 0 } ? g : null;
        }
        catch { return null; }
    }

    public static void Save(double width, double height)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataFolder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new Geometry { Width = width, Height = height }));
        }
        catch { /* размер окна — не критичная настройка */ }
    }
}
