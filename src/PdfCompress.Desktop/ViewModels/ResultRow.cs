using PdfCompress.Core.Models;

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

    public string FileName { get; }
    public string Icon { get; }
    public string Detail { get; }
    public CompressionOutcome Outcome { get; }

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
