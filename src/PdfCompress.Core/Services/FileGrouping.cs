using PdfCompress.Core.Models;

namespace PdfCompress.Core.Services;

/// <summary>Набор файлов, который должен уложиться в один общий предел.</summary>
/// <param name="Title">Подпись для отчёта — папка, о которой идёт речь.</param>
/// <param name="RootFolder">Папка-источник, к которой относится группа.</param>
/// <param name="Files">Файлы группы.</param>
public sealed record FileGroup(string Title, string RootFolder, IReadOnlyList<PdfFileEntry> Files)
{
    public long TotalBytes
    {
        get
        {
            long total = 0;
            foreach (var file in Files) total += file.SizeBytes;
            return total;
        }
    }
}

/// <summary>
/// Разбивает список файлов на группы, к каждой из которых предел применяется целиком.
/// Вынесено в ядро, а не в интерфейс: правило «что считать одной папкой» — часть логики
/// обработки, и его надо проверять тестами.
/// </summary>
public static class FileGrouping
{
    /// <summary>
    /// Группирует файлы по заданной области действия предела. Для
    /// <see cref="TargetScope.File"/> каждый файл становится собственной группой — так вызывающий
    /// код может не разветвляться.
    ///
    /// Порядок групп сохраняет порядок файлов: отчёт идёт сверху вниз так же, как список.
    /// </summary>
    public static IReadOnlyList<FileGroup> Build(IReadOnlyList<PdfFileEntry> files, TargetScope scope)
    {
        ArgumentNullException.ThrowIfNull(files);

        if (scope == TargetScope.File)
            return files.Select(f => new FileGroup(f.DisplayName, f.RootFolder, new[] { f })).ToList();

        var order = new List<string>();
        var buckets = new Dictionary<string, List<PdfFileEntry>>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            string key = scope == TargetScope.Folder
                ? file.RootFolder
                : file.RootFolder + Path.DirectorySeparatorChar + file.TopLevelFolder;

            if (!buckets.TryGetValue(key, out var bucket))
            {
                bucket = new List<PdfFileEntry>();
                buckets[key] = bucket;
                order.Add(key);
            }
            bucket.Add(file);
        }

        return order
            .Select(key => new FileGroup(TitleFor(buckets[key][0], scope), buckets[key][0].RootFolder, buckets[key]))
            .ToList();
    }

    private static string TitleFor(PdfFileEntry sample, TargetScope scope)
    {
        string root = Path.GetFileName(Path.TrimEndingDirectorySeparator(sample.RootFolder));
        if (string.IsNullOrEmpty(root))
            root = sample.RootFolder; // корень диска вроде «D:\» имени не имеет

        if (scope == TargetScope.Folder)
            return root;

        return string.IsNullOrEmpty(sample.TopLevelFolder)
            ? $"{root} (файлы в корне)"
            : $"{root}\\{sample.TopLevelFolder}";
    }
}
