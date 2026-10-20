using PdfCompress.Core.Models;

namespace PdfCompress.Core.Services;

/// <summary>Поиск PDF-файлов в папках — источник списка на главном экране.</summary>
public class PdfFileScanner
{
    /// <summary>
    /// Возвращает PDF-файлы папки, отсортированные по пути. Файлы, которые исчезли или
    /// недоступны между перечислением и чтением размера, молча пропускаются: список — снимок,
    /// а не транзакция.
    /// </summary>
    /// <param name="folder">Папка для поиска.</param>
    /// <param name="recursive">Искать и во вложенных папках.</param>
    public IReadOnlyList<PdfFileEntry> Scan(string folder, bool recursive = false)
    {
        if (string.IsNullOrWhiteSpace(folder))
            throw new ArgumentException("Не указана папка.", nameof(folder));
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"Папка не найдена: {folder}");

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            // Скрытые и системные файлы (например, ~$-времянки) пользователю не нужны.
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            MatchCasing = MatchCasing.CaseInsensitive,
        };

        var result = new List<PdfFileEntry>();
        foreach (var path in Directory.EnumerateFiles(root, "*.pdf", options))
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) continue;
                result.Add(new PdfFileEntry(info.FullName, root, Path.GetRelativePath(root, info.FullName), info.Length));
            }
            catch (IOException) { /* файл занят или удалён — пропускаем */ }
            catch (UnauthorizedAccessException) { /* нет прав на чтение — пропускаем */ }
        }

        Sort(result);
        return result;
    }

    /// <summary>
    /// Сканирует несколько папок подряд. Папки, которые исчезли или недоступны, пропускаются
    /// с записью в <paramref name="skipped"/>: одна недоступная сетевая шара не должна
    /// обрушивать весь список.
    ///
    /// Один и тот же файл, попавший в выборку из двух вложенных друг в друга корней,
    /// возвращается один раз — иначе он обрабатывался бы дважды и во второй раз затирал бы
    /// собственный результат.
    /// </summary>
    public IReadOnlyList<PdfFileEntry> ScanMany(
        IEnumerable<string> folders,
        bool recursive,
        out IReadOnlyList<(string Folder, string Error)> skipped)
    {
        var problems = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<PdfFileEntry>();

        foreach (var folder in folders)
        {
            try
            {
                foreach (var entry in Scan(folder, recursive))
                {
                    if (seen.Add(entry.FullPath))
                        result.Add(entry);
                }
            }
            catch (Exception ex)
            {
                problems.Add((folder, ex.Message));
            }
        }

        Sort(result);
        skipped = problems;
        return result;
    }

    private static void Sort(List<PdfFileEntry> entries) =>
        entries.Sort(static (a, b) =>
        {
            int byRoot = string.Compare(a.RootFolder, b.RootFolder, StringComparison.CurrentCultureIgnoreCase);
            return byRoot != 0
                ? byRoot
                : string.Compare(a.RelativePath, b.RelativePath, StringComparison.CurrentCultureIgnoreCase);
        });
}
