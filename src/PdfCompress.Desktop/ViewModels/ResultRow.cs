using PdfCompress.Core.Models;
using PdfCompress.Core.Services;

namespace PdfCompress.Desktop.ViewModels;

/// <summary>
/// Строка панели результатов. Сразу несёт готовые к показу подписи: сортировать и форматировать
/// в разметке нечего, а значит и расхождений между тем, что посчитано и что видно, не будет.
/// </summary>
public sealed class ResultRow
{
    public ResultRow(FileCompressionResult result)
    {
        FileName = result.FileName;
        Outcome = result.Outcome;

        (Icon, Detail) = result.Outcome switch
        {
            CompressionOutcome.Compressed => ("✓",
                $"{SizeUnits.Format(result.OriginalBytes)} → {SizeUnits.Format(result.ResultBytes)}   " +
                $"(−{result.SavedPercent:0.#} %)"),

            CompressionOutcome.AlreadySmallEnough => ("=",
                $"{SizeUnits.Format(result.OriginalBytes)} — уже меньше предела, скопирован без изменений"),

            CompressionOutcome.CopiedAsIs => ("=",
                $"{SizeUnits.Format(result.OriginalBytes)} — {NothingToGain(result)}, скопирован без изменений"),

            CompressionOutcome.TargetNotReached => ("!",
                $"{SizeUnits.Format(result.OriginalBytes)} → {SizeUnits.Format(result.ResultBytes)}   " +
                $"(−{result.SavedPercent:0.#} %) — в заданный предел уложить не удалось"),

            _ => ("✕", result.Error ?? "не удалось обработать"),
        };
    }

    /// <summary>Заголовок группы: итог по папке, к которой применялся общий предел.</summary>
    private ResultRow(FileGroup group, GroupCompressionResult result, long targetBytes)
    {
        IsGroupHeader = true;
        FileName = group.Title;
        Outcome = result.TargetReached ? CompressionOutcome.Compressed : CompressionOutcome.TargetNotReached;

        double percent = result.OriginalBytes <= 0
            ? 0
            : 100.0 * (result.OriginalBytes - result.ResultBytes) / result.OriginalBytes;

        string sizes = $"{SizeUnits.Format(result.OriginalBytes)} → {SizeUnits.Format(result.ResultBytes)} " +
                       $"(−{percent:0.#} %) из {SizeUnits.Format(targetBytes)}";

        string files = $"{result.Files.Count} {Plural(result.Files.Count, "файл", "файла", "файлов")}";

        (Icon, Detail) = result.TargetReached
            ? ("✓", $"{files} · {sizes}")
            : ("!", $"{files} · {sizes} — в предел уложить не удалось");
    }

    /// <summary>
    /// Русское склонение по числу: 1 файл, 2 файла, 5 файлов. Отдельные 11–14 — исключение,
    /// они всегда идут с формой множественного числа.
    /// </summary>
    private static string Plural(int count, string one, string few, string many)
    {
        int lastTwo = Math.Abs(count) % 100;
        if (lastTwo is >= 11 and <= 14)
            return many;

        return (Math.Abs(count) % 10) switch
        {
            1 => one,
            2 or 3 or 4 => few,
            _ => many,
        };
    }

    /// <summary>Создаёт строку-заголовок для группы файлов с общим пределом.</summary>
    public static ResultRow ForGroup(FileGroup group, GroupCompressionResult result, long targetBytes) =>
        new(group, result, targetBytes);

    public string FileName { get; }
    public string Icon { get; }
    public string Detail { get; }
    public CompressionOutcome Outcome { get; }

    /// <summary>Строка относится к папке целиком, а не к отдельному файлу — выделяется жирным.</summary>
    public bool IsGroupHeader { get; }

    /// <summary>Ошибки и недостижение цели подсвечиваются в списке.</summary>
    public bool IsProblem => Outcome is CompressionOutcome.Failed or CompressionOutcome.TargetNotReached;

    /// <summary>
    /// Почему файл не уменьшился. Различать «картинок не было» и «картинки есть, но их формат
    /// не взяли» важно: во втором случае это повод посмотреть, что за формат внутри, — именно
    /// так нашлась цепочка фильтров, из-за которой целая пачка сканов сжималась на 0 %.
    /// </summary>
    private static string NothingToGain(FileCompressionResult result)
    {
        if (result.ImagesTotal == 0)
            return "растров в документе нет";

        if (result.ImagesRecompressed == 0)
            return $"растры ({result.ImagesTotal} шт.) уже оптимальны или в неподдержанном формате";

        return "выигрыш не набрался";
    }
}
