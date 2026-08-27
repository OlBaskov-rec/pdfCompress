using PdfCompress.Core.Models;

namespace PdfCompress.Core.Services;

/// <summary>Поиск PDF-файлов в папке — источник списка на главном экране.</summary>
public class PdfFileScanner
{
    /// <summary>
    /// Возвращает PDF-файлы папки, отсортированные по имени. Файлы, которые исчезли или
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

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            // Скрытые и системные файлы (например, ~$-времянки) пользователю не нужны.
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            MatchCasing = MatchCasing.CaseInsensitive,
        };

        var result = new List<PdfFileEntry>();
        foreach (var path in Directory.EnumerateFiles(folder, "*.pdf", options))
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) continue;
                result.Add(new PdfFileEntry(info.FullName, info.Name, info.Length));
            }
            catch (IOException) { /* файл занят или удалён — пропускаем */ }
            catch (UnauthorizedAccessException) { /* нет прав на чтение — пропускаем */ }
        }

        result.Sort(static (a, b) => string.Compare(a.FileName, b.FileName, StringComparison.CurrentCultureIgnoreCase));
        return result;
    }
}
