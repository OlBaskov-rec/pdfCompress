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
                $"{SizeUnits.Format(result.OriginalBytes)} — сжимать нечего, скопирован без изменений"),

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
}
