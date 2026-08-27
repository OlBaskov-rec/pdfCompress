namespace PdfCompress.Desktop.Services;

/// <summary>Выбор папки — вынесен в интерфейс, чтобы ViewModel не зависела от Avalonia напрямую.</summary>
public interface IFolderPicker
{
    /// <summary>Показывает диалог выбора папки. Возвращает null, если пользователь отказался.</summary>
    /// <param name="title">Заголовок диалога.</param>
    /// <param name="startFolder">Папка, с которой открыть диалог (если существует).</param>
    Task<string?> PickFolderAsync(string title, string? startFolder = null);
}
