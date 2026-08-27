namespace PdfCompress.Desktop.Services;

/// <summary>Единая точка, где живут пользовательские данные приложения.</summary>
public static class AppPaths
{
    /// <summary>%AppData%\PdfCompress — настройки, журнал, состояние окна.</summary>
    public static string DataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PdfCompress");
}
