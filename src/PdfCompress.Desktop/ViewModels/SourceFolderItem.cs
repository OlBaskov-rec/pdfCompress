using CommunityToolkit.Mvvm.ComponentModel;
using PdfCompress.Core.Models;

namespace PdfCompress.Desktop.ViewModels;

/// <summary>Строка панели папок-источников: сама папка, флажок «обрабатывать» и её сводка.</summary>
public partial class SourceFolderItem : ObservableObject
{
    public SourceFolderItem(string path, bool isSelected = true)
    {
        Path = System.IO.Path.TrimEndingDirectorySeparator(path);
        this.isSelected = isSelected;
    }

    /// <summary>Полный путь к папке.</summary>
    public string Path { get; }

    /// <summary>Имя папки — то, что видно в списке.</summary>
    public string Name
    {
        get
        {
            string name = System.IO.Path.GetFileName(Path);
            return string.IsNullOrEmpty(name) ? Path : name; // у корня диска имени нет
        }
    }

    /// <summary>Папка, в которой лежит эта — показывается мелким шрифтом под именем.</summary>
    public string ParentPath => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;

    /// <summary>Обрабатывать ли папку.</summary>
    [ObservableProperty]
    private bool isSelected = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private int fileCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private long totalBytes;

    /// <summary>Доступна ли папка. Недоступную (отключили сетевой диск) показываем, но помечаем.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private bool isAvailable = true;

    public string Summary => !IsAvailable
        ? "папка недоступна"
        : FileCount == 0
            ? "PDF не найдено"
            : $"{FileCount} PDF · {SizeUnits.Format(TotalBytes)}";
}
