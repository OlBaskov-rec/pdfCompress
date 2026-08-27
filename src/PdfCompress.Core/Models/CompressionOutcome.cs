namespace PdfCompress.Core.Models;

/// <summary>Чем закончилась обработка одного файла.</summary>
public enum CompressionOutcome
{
    /// <summary>Файл сжат, результат записан.</summary>
    Compressed,

    /// <summary>
    /// Сжать не удалось — результат получился не меньше исходника. Записана копия оригинала,
    /// чтобы в выходной папке лежал полный комплект документов.
    /// </summary>
    CopiedAsIs,

    /// <summary>Файл и так укладывается в заданный предел — скопирован без изменений.</summary>
    AlreadySmallEnough,

    /// <summary>Целевой размер не достигнут даже на максимальном сжатии (записан лучший вариант).</summary>
    TargetNotReached,

    /// <summary>Файл обработать не удалось (повреждён, зашифрован, нет доступа).</summary>
    Failed,
}

/// <summary>Результат обработки одного PDF-файла.</summary>
public sealed record FileCompressionResult
{
    public required string SourcePath { get; init; }
    public required string FileName { get; init; }
    public required long OriginalBytes { get; init; }

    /// <summary>Размер результата; при <see cref="CompressionOutcome.Failed"/> — 0.</summary>
    public long ResultBytes { get; init; }

    /// <summary>Путь к записанному файлу; null, если запись не состоялась.</summary>
    public string? OutputPath { get; init; }

    public required CompressionOutcome Outcome { get; init; }

    /// <summary>Сколько растров пересжато.</summary>
    public int ImagesRecompressed { get; init; }

    /// <summary>
    /// Сколько растров вообще нашлось в документе. Вместе с <see cref="ImagesRecompressed"/>
    /// отвечает на главный вопрос при разборе «почему не ужалось»: картинок не было вовсе
    /// или их формат мы не берём.
    /// </summary>
    public int ImagesTotal { get; init; }

    /// <summary>Сколько проходов сжатия понадобилось (в режиме подбора под размер — больше одного).</summary>
    public int Attempts { get; init; } = 1;

    /// <summary>Текст ошибки для <see cref="CompressionOutcome.Failed"/>.</summary>
    public string? Error { get; init; }

    /// <summary>Экономия в байтах (0, если файл не уменьшился).</summary>
    public long SavedBytes => Math.Max(0, OriginalBytes - ResultBytes);

    /// <summary>Экономия в процентах от исходного размера.</summary>
    public double SavedPercent =>
        OriginalBytes <= 0 || ResultBytes <= 0 ? 0 : 100.0 * (OriginalBytes - ResultBytes) / OriginalBytes;
}
