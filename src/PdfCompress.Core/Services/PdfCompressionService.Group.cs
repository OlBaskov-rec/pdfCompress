using PdfCompress.Core.Models;

namespace PdfCompress.Core.Services;

/// <summary>Один файл в задании: откуда взять и куда положить.</summary>
/// <param name="SourcePath">Исходный PDF.</param>
/// <param name="OutputPath">Куда писать результат.</param>
/// <param name="DisplayName">Как называть файл в отчёте (обычно путь от папки-источника).</param>
public sealed record CompressionJob(string SourcePath, string OutputPath, string DisplayName);

/// <summary>Ход обработки группы — чтобы интерфейс показывал движение, а не замирал.</summary>
/// <param name="Pass">Номер текущего прохода, начиная с 1.</param>
/// <param name="TotalPasses">Сколько проходов запланировано в худшем случае.</param>
/// <param name="FileIndex">Сколько файлов этого прохода уже обработано.</param>
/// <param name="FileCount">Сколько всего файлов в группе.</param>
/// <param name="FileName">Файл, который обрабатывается прямо сейчас.</param>
public sealed record GroupProgress(int Pass, int TotalPasses, int FileIndex, int FileCount, string FileName);

/// <summary>Итог по группе файлов, которую укладывали в общий предел.</summary>
public sealed record GroupCompressionResult
{
    public required IReadOnlyList<FileCompressionResult> Files { get; init; }

    /// <summary>Суммарный размер исходников группы.</summary>
    public required long OriginalBytes { get; init; }

    /// <summary>Суммарный размер того, что записано на диск.</summary>
    public required long ResultBytes { get; init; }

    /// <summary>Удалось ли уложить сумму в заданный предел.</summary>
    public required bool TargetReached { get; init; }

    /// <summary>Сколько проходов по группе понадобилось.</summary>
    public required int Passes { get; init; }
}

public partial class PdfCompressionService
{
    /// <summary>
    /// Сжимает группу файлов так, чтобы в предел уложилась их СУММА.
    ///
    /// Ключевое отличие от пофайлового режима: подбирается ОДНА степень сжатия на всю группу.
    /// Это и честнее по качеству (все документы пачки выглядят одинаково, а не «этот чёткий,
    /// а соседний мыльный»), и успешнее по результату — запас одного файла работает на другой,
    /// тогда как при раздаче каждому своей квоты один упрямый скан проваливает всю папку.
    ///
    /// Проходы поиска считают только РАЗМЕРЫ и тут же выбрасывают сжатые байты: держать в памяти
    /// целую папку нельзя, она бывает и на гигабайты. Поэтому в конце делается ещё один проход,
    /// который пишет результат на диск уже с найденной степенью.
    /// </summary>
    public GroupCompressionResult CompressGroupToTargetSize(
        IReadOnlyList<CompressionJob> jobs,
        long targetBytes,
        IProgress<GroupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        if (targetBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetBytes), "Предельный размер должен быть больше нуля.");

        if (jobs.Count == 0)
        {
            return new GroupCompressionResult
            {
                Files = Array.Empty<FileCompressionResult>(),
                OriginalBytes = 0,
                ResultBytes = 0,
                TargetReached = true,
                Passes = 0,
            };
        }

        // 1 проход на максимальное сжатие + уточняющие + 1 записывающий.
        int plannedPasses = RefinementSteps + 2;
        long originalTotal = TotalOriginalBytes(jobs);

        // Группа и так укладывается — трогать документы незачем, просто переносим как есть.
        if (originalTotal <= targetBytes)
            return WritePass(jobs, strength: null, plannedPasses, plannedPasses, originalTotal, true, 1, progress, cancellationToken);

        long aim = Math.Max(1, (long)Math.Floor(targetBytes * (1 - TargetSafetyMargin)));

        int passes = 1;
        long strongest = MeasureTotal(jobs, 1.0, abortAbove: targetBytes, 1, plannedPasses, progress, cancellationToken);

        // Даже максимальное сжатие не помещается — записываем лучшее, что есть, и честно
        // сообщаем, что предел не достигнут.
        if (strongest > targetBytes)
            return WritePass(jobs, 1.0, passes + 1, plannedPasses, originalTotal, false, passes + 1, progress, cancellationToken);

        // Запаса нет, но в предел уложились — требование выполнено, мягче искать нечего.
        if (strongest > aim)
            return WritePass(jobs, 1.0, passes + 1, plannedPasses, originalTotal, true, passes + 1, progress, cancellationToken);

        double best = 1.0, low = 0.0, high = 1.0;
        for (int step = 0; step < RefinementSteps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            double middle = (low + high) / 2;
            passes++;
            long total = MeasureTotal(jobs, middle, abortAbove: aim, passes, plannedPasses, progress, cancellationToken);

            if (total <= aim)
            {
                best = middle;
                high = middle; // влезло — пробуем ещё мягче
            }
            else
            {
                low = middle;  // не влезло — нужно жёстче
            }
        }

        return WritePass(jobs, best, passes + 1, plannedPasses, originalTotal, true, passes + 1, progress, cancellationToken);
    }

    /// <summary>
    /// Считает, во сколько байт уложится группа при заданной степени. Байты не сохраняются —
    /// нужен только размер.
    ///
    /// Как только накопленная сумма превысила <paramref name="abortAbove"/>, проход бросается:
    /// ответ «не влезает» уже получен, а дожимать оставшиеся файлы — минуты впустую. На сетевой
    /// папке это экономит больше половины времени поиска.
    /// </summary>
    private long MeasureTotal(
        IReadOnlyList<CompressionJob> jobs,
        double strength,
        long abortAbove,
        int pass,
        int plannedPasses,
        IProgress<GroupProgress>? progress,
        CancellationToken cancellationToken)
    {
        var options = CompressionOptions.ForStrength(strength);
        long total = 0;

        for (int i = 0; i < jobs.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var job = jobs[i];
            progress?.Report(new GroupProgress(pass, plannedPasses, i, jobs.Count, job.DisplayName));

            long size;
            try
            {
                byte[] original = File.ReadAllBytes(job.SourcePath);
                try
                {
                    var pass1 = CompressOnce(original, options, cancellationToken);
                    size = Math.Min(pass1.Bytes.LongLength, original.LongLength);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception)
                {
                    // Нечитаемый PDF в группу всё равно попадёт как есть — считаем по оригиналу.
                    size = original.LongLength;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                continue; // файл недоступен — на итоговый размер он не влияет
            }

            total += size;
            if (total > abortAbove)
                return long.MaxValue;
        }

        return total;
    }

    /// <summary>
    /// Последний проход: сжимает с найденной степенью и пишет результат на диск.
    /// <paramref name="strength"/> = null означает «ничего не сжимать, перенести как есть»
    /// (группа и так уложилась в предел).
    /// </summary>
    private GroupCompressionResult WritePass(
        IReadOnlyList<CompressionJob> jobs,
        double? strength,
        int pass,
        int plannedPasses,
        long originalTotal,
        bool targetReached,
        int passes,
        IProgress<GroupProgress>? progress,
        CancellationToken cancellationToken)
    {
        var options = strength is null ? null : CompressionOptions.ForStrength(strength.Value);
        var results = new List<FileCompressionResult>(jobs.Count);
        long resultTotal = 0;

        for (int i = 0; i < jobs.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var job = jobs[i];
            progress?.Report(new GroupProgress(pass, plannedPasses, i, jobs.Count, job.DisplayName));

            var result = CompressSingleWithOptions(job, options, cancellationToken);
            results.Add(result);
            resultTotal += result.ResultBytes;
        }

        return new GroupCompressionResult
        {
            Files = results,
            OriginalBytes = originalTotal,
            ResultBytes = resultTotal,
            TargetReached = targetReached,
            Passes = passes,
        };
    }

    /// <summary>
    /// Один файл группы с уже выбранными параметрами. Предел здесь не проверяется: он относится
    /// ко всей группе, и отдельный файл его «нарушить» не может.
    /// </summary>
    private FileCompressionResult CompressSingleWithOptions(
        CompressionJob job,
        CompressionOptions? options,
        CancellationToken cancellationToken)
    {
        byte[] original;
        try
        {
            original = File.ReadAllBytes(job.SourcePath);
        }
        catch (Exception ex)
        {
            return Failure(job.SourcePath, job.DisplayName, 0, $"не удалось прочитать файл: {ex.Message}");
        }

        byte[] payload = original;
        int images = 0, imagesTotal = 0;
        var outcome = CompressionOutcome.CopiedAsIs;

        if (options is not null)
        {
            try
            {
                var compressed = CompressOnce(original, options, cancellationToken);
                images = compressed.Images.Replaced;
                imagesTotal = compressed.Images.Total;

                if (compressed.Bytes.LongLength < original.LongLength)
                {
                    payload = compressed.Bytes;
                    outcome = CompressionOutcome.Compressed;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Failure(job.SourcePath, job.DisplayName, original.LongLength, ex.Message);
            }
        }
        else
        {
            outcome = CompressionOutcome.AlreadySmallEnough;
        }

        try
        {
            Write(job.OutputPath, payload);
        }
        catch (Exception ex)
        {
            return Failure(job.SourcePath, job.DisplayName, original.LongLength, $"не удалось записать результат: {ex.Message}");
        }

        return new FileCompressionResult
        {
            SourcePath = job.SourcePath,
            FileName = job.DisplayName,
            OriginalBytes = original.LongLength,
            ResultBytes = payload.LongLength,
            OutputPath = job.OutputPath,
            Outcome = outcome,
            ImagesRecompressed = images,
            ImagesTotal = imagesTotal,
            Attempts = 1,
        };
    }

    private static long TotalOriginalBytes(IReadOnlyList<CompressionJob> jobs)
    {
        long total = 0;
        foreach (var job in jobs)
        {
            try
            {
                var info = new FileInfo(job.SourcePath);
                if (info.Exists) total += info.Length;
            }
            catch (Exception) { /* недоступный файл в сумму не входит */ }
        }
        return total;
    }

    /// <summary>
    /// Путь результата с сохранением структуры подпапок: иначе одноимённые файлы из разных
    /// вложенных папок затирали бы друг друга в общей выходной папке.
    /// </summary>
    public static string BuildOutputPath(PdfFileEntry entry, string outputFolder) =>
        Path.Combine(outputFolder, entry.RelativePath);
}
