using PdfCompress.Core.Models;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PdfCompress.Core.Services;

/// <summary>
/// Сжатие PDF-файлов: точка входа для интерфейса. Вся работа идёт в памяти и без внешних
/// программ (ни Ghostscript, ни qpdf), поэтому приложение остаётся переносимым.
///
/// Исходный файл никогда не меняется по ходу обработки: результат пишется во временный файл
/// рядом с целевым и переносится на место одним движением — прерывание не оставит обрубка.
/// </summary>
public class PdfCompressionService
{
    /// <summary>
    /// Сколько уточняющих проходов делать при подборе под целевой размер. Каждый проход — это
    /// полное пересжатие документа, поэтому шкала делится пополам ограниченное число раз:
    /// четырёх шагов хватает, чтобы не выбрать «на глазок» слишком грубые параметры.
    /// </summary>
    private const int RefinementSteps = 4;

    private readonly PdfImageRecompressor _recompressor = new();

    /// <summary>Обрабатывает один файл согласно заданию и записывает результат.</summary>
    /// <param name="sourcePath">Исходный PDF.</param>
    /// <param name="outputPath">Куда писать результат (может совпадать с исходником).</param>
    public FileCompressionResult Compress(
        string sourcePath,
        string outputPath,
        CompressionRequest request,
        CancellationToken cancellationToken = default)
    {
        string fileName = Path.GetFileName(sourcePath);

        byte[] original;
        try
        {
            original = File.ReadAllBytes(sourcePath);
        }
        catch (Exception ex)
        {
            return Failure(sourcePath, fileName, 0, $"не удалось прочитать файл: {ex.Message}");
        }

        try
        {
            var attempt = request.Mode == CompressionMode.Level
                ? CompressToLevel(original, request.Level, cancellationToken)
                : CompressToTarget(original, request.TargetBytes, cancellationToken);

            // Пишем меньший из двух вариантов: если сжатие не помогло (уже оптимизированный
            // документ), в выходной папке должен всё равно оказаться рабочий файл.
            bool useCompressed = attempt.Bytes.LongLength < original.LongLength;
            byte[] payload = useCompressed ? attempt.Bytes : original;

            Write(outputPath, payload);

            return new FileCompressionResult
            {
                SourcePath = sourcePath,
                FileName = fileName,
                OriginalBytes = original.LongLength,
                ResultBytes = payload.LongLength,
                OutputPath = outputPath,
                Outcome = useCompressed ? attempt.Outcome : DowngradeOutcome(attempt.Outcome),
                ImagesRecompressed = attempt.Images,
                Attempts = attempt.Attempts,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PdfReaderException ex)
        {
            return Failure(sourcePath, fileName, original.LongLength,
                $"PDF не читается или защищён паролем: {ex.Message}");
        }
        catch (Exception ex)
        {
            return Failure(sourcePath, fileName, original.LongLength, ex.Message);
        }
    }

    /// <summary>Результат одного задания до записи на диск.</summary>
    private readonly record struct Attempt(byte[] Bytes, int Images, int Attempts, CompressionOutcome Outcome);

    private Attempt CompressToLevel(byte[] original, CompressionLevel level, CancellationToken cancellationToken)
    {
        var (bytes, images) = CompressOnce(original, CompressionOptions.ForLevel(level), cancellationToken);
        return new Attempt(bytes, images, 1, CompressionOutcome.Compressed);
    }

    /// <summary>
    /// Подбирает самые щадящие параметры, при которых файл всё ещё влезает в заданный предел.
    ///
    /// Сначала пробуем максимальное сжатие: если даже оно не укладывается в предел, дальше искать
    /// нечего. Если укладывается — двоичным поиском по шкале «силы» идём в сторону лучшего
    /// качества, пока результат продолжает помещаться.
    /// </summary>
    private Attempt CompressToTarget(byte[] original, long targetBytes, CancellationToken cancellationToken)
    {
        if (targetBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetBytes), "Предельный размер должен быть больше нуля.");

        if (original.LongLength <= targetBytes)
            return new Attempt(original, 0, 0, CompressionOutcome.AlreadySmallEnough);

        int attempts = 1;
        var strongest = CompressOnce(original, CompressionOptions.ForStrength(1.0), cancellationToken);
        if (strongest.Bytes.LongLength > targetBytes)
            return new Attempt(strongest.Bytes, strongest.Images, attempts, CompressionOutcome.TargetNotReached);

        // best — самый качественный из уже найденных вариантов, который влезает в предел.
        var best = strongest;
        double low = 0.0, high = 1.0;

        for (int step = 0; step < RefinementSteps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            double middle = (low + high) / 2;
            var candidate = CompressOnce(original, CompressionOptions.ForStrength(middle), cancellationToken);
            attempts++;

            if (candidate.Bytes.LongLength <= targetBytes)
            {
                best = candidate;
                high = middle; // влезло — пробуем ещё мягче
            }
            else
            {
                low = middle;  // не влезло — нужно жёстче
            }
        }

        return new Attempt(best.Bytes, best.Images, attempts, CompressionOutcome.Compressed);
    }

    /// <summary>Один полный проход сжатия документа с заданными параметрами.</summary>
    private (byte[] Bytes, int Images) CompressOnce(
        byte[] source,
        CompressionOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var input = new MemoryStream(source, writable: false);
        using var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);

        int images = _recompressor.Process(document, options, cancellationToken);

        document.Options.NoCompression = false;
        document.Options.CompressContentStreams = true;
        document.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;

        using var output = new MemoryStream();
        document.Save(output, false);
        return (output.ToArray(), images);
    }

    /// <summary>
    /// Записывает результат атомарно: сначала во временный файл рядом с целевым, затем
    /// перемещением на место. Так безопасна и перезапись исходного файла «на месте».
    /// </summary>
    private static void Write(string outputPath, byte[] payload)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
        Directory.CreateDirectory(directory);

        string temporary = Path.Combine(directory, $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, payload);
            File.Move(temporary, outputPath, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* временный файл — не повод падать поверх основной ошибки */ }
    }

    /// <summary>Сжатие не дало выигрыша — исход отражает, что записан оригинал.</summary>
    private static CompressionOutcome DowngradeOutcome(CompressionOutcome outcome) => outcome switch
    {
        CompressionOutcome.AlreadySmallEnough => CompressionOutcome.AlreadySmallEnough,
        CompressionOutcome.TargetNotReached => CompressionOutcome.TargetNotReached,
        _ => CompressionOutcome.CopiedAsIs,
    };

    private static FileCompressionResult Failure(string sourcePath, string fileName, long originalBytes, string error) =>
        new()
        {
            SourcePath = sourcePath,
            FileName = fileName,
            OriginalBytes = originalBytes,
            Outcome = CompressionOutcome.Failed,
            Error = error,
        };

    /// <summary>
    /// Путь результата для файла: то же имя в выходной папке. Отдельный метод, чтобы
    /// интерфейс и тесты одинаково понимали, куда именно ляжет файл.
    /// </summary>
    public static string BuildOutputPath(string sourcePath, string outputFolder) =>
        Path.Combine(outputFolder, Path.GetFileName(sourcePath));
}
