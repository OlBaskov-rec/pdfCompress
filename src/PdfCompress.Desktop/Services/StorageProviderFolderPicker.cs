using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace PdfCompress.Desktop.Services;

/// <summary>Выбор папки через кросс-платформенный Avalonia StorageProvider.</summary>
public class StorageProviderFolderPicker : IFolderPicker
{
    private readonly Func<TopLevel?> _topLevel;

    public StorageProviderFolderPicker(Func<TopLevel?> topLevel) => _topLevel = topLevel;

    public async Task<string?> PickFolderAsync(string title, string? startFolder = null)
    {
        var top = _topLevel();
        if (top is null) return null;

        IStorageFolder? start = null;
        if (!string.IsNullOrWhiteSpace(startFolder) && Directory.Exists(startFolder))
        {
            try { start = await top.StorageProvider.TryGetFolderFromPathAsync(startFolder); }
            catch (Exception ex) { AppLog.Error($"Не удалось открыть диалог на папке «{startFolder}»", ex); }
        }

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = start,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
