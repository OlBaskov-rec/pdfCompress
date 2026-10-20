namespace PdfCompress.Core.Models;

/// <summary>Найденный PDF-файл: откуда он взялся, как называется и сколько весит.</summary>
/// <param name="FullPath">Полный путь к файлу.</param>
/// <param name="RootFolder">Папка-источник, в которой его нашли (одна из выбранных пользователем).</param>
/// <param name="RelativePath">
/// Путь относительно <paramref name="RootFolder"/>, вместе с именем файла. Для файла прямо
/// в корне совпадает с именем. Нужен сразу для двух вещей: по нему результаты раскладываются
/// в выходной папке с сохранением структуры (иначе одноимённые файлы из разных подпапок
/// затирали бы друг друга), и по нему же файлы группируются по подпапкам.
/// </param>
/// <param name="SizeBytes">Размер в байтах.</param>
public sealed record PdfFileEntry(string FullPath, string RootFolder, string RelativePath, long SizeBytes)
{
    /// <summary>Имя файла с расширением.</summary>
    public string FileName => Path.GetFileName(RelativePath);

    /// <summary>
    /// Папка верхнего уровня внутри корня, к которой относится файл. Пустая строка — файл лежит
    /// прямо в корне. Это и есть ключ группировки для режима «предел на каждую подпапку»:
    /// пользователь мыслит подпапками первого уровня, а не всей глубиной вложенности.
    /// </summary>
    public string TopLevelFolder
    {
        get
        {
            int separator = RelativePath.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
            return separator < 0 ? string.Empty : RelativePath[..separator];
        }
    }

    /// <summary>Подпись для списка: имя файла, а для вложенных — с путём от корня.</summary>
    public string DisplayName => RelativePath;

    /// <summary>Размер в человекочитаемом виде («12,4 МБ»).</summary>
    public string SizeText => SizeUnits.Format(SizeBytes);
}
