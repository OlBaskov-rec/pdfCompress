using CommunityToolkit.Mvvm.ComponentModel;
using PdfCompress.Core.Models;

namespace PdfCompress.Desktop.ViewModels;

/// <summary>Строка списка найденных PDF: сам файл плюс флажок «обрабатывать».</summary>
public partial class PdfFileItem : ObservableObject
{
    public PdfFileItem(PdfFileEntry entry) => Entry = entry;

    public PdfFileEntry Entry { get; }

    public string FileName => Entry.FileName;
    public string FullPath => Entry.FullPath;
    public long SizeBytes => Entry.SizeBytes;
    public string SizeText => Entry.SizeText;

    /// <summary>Обрабатывать ли файл. По умолчанию отмечены все найденные.</summary>
    [ObservableProperty]
    private bool isSelected = true;
}
