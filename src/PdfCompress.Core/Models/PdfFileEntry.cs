namespace PdfCompress.Core.Models;

/// <summary>Найденный в папке PDF-файл: путь, имя и размер на момент сканирования.</summary>
/// <param name="FullPath">Полный путь к файлу.</param>
/// <param name="FileName">Имя файла с расширением.</param>
/// <param name="SizeBytes">Размер в байтах.</param>
public sealed record PdfFileEntry(string FullPath, string FileName, long SizeBytes)
{
    /// <summary>Размер в человекочитаемом виде («12,4 МБ»).</summary>
    public string SizeText => SizeUnits.Format(SizeBytes);
}
