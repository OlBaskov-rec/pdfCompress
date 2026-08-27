using System.Diagnostics;

namespace PdfCompress.Desktop.Services;

/// <summary>Кросс-платформенные системные действия.</summary>
public static class PlatformHelper
{
    /// <summary>Открывает папку в системном файловом менеджере (Explorer / Finder / xdg-open).</summary>
    public static void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                // В Windows кавычки в пути невозможны — экранирование кавычками безопасно.
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            }
            else
            {
                // ArgumentList передаёт путь одним аргументом без ручного экранирования:
                // кавычки и пробелы в имени папки (в Unix допустимы) не ломают команду.
                var psi = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open");
                psi.ArgumentList.Add(path);
                Process.Start(psi);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"Не удалось открыть папку «{path}»", ex);
        }
    }
}
