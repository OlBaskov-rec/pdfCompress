using PdfCompress.Core.Models;
using PdfCompress.Core.Services;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PdfCompress.Core.Tests;

public class GroupCompressionTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pdfgroup-tests-" + Guid.NewGuid().ToString("N"));
    private readonly PdfCompressionService _service = new();

    public GroupCompressionTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* уборка не критична */ }
    }

    private string Source(string name, byte[] bytes)
    {
        string path = Path.Combine(_folder, "in", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private CompressionJob Job(string name, byte[] bytes) =>
        new(Source(name, bytes), Path.Combine(_folder, "out", name), name);

    private List<CompressionJob> ThreeScans() => new()
    {
        Job("один.pdf", TestPdfBuilder.WithPhotoPages()),
        Job("два.pdf", TestPdfBuilder.WithPhotoPages()),
        Job("три.pdf", TestPdfBuilder.WithPhotoPages()),
    };

    private static long TotalOf(IEnumerable<CompressionJob> jobs) =>
        jobs.Sum(j => new FileInfo(j.SourcePath).Length);

    [Fact]
    public void Группа_УкладываетСуммуВПредел()
    {
        var jobs = ThreeScans();
        long original = TotalOf(jobs);
        long target = original / 3;

        var result = _service.CompressGroupToTargetSize(jobs, target);

        Assert.True(result.TargetReached, "сумма должна была уложиться в предел");
        Assert.True(result.ResultBytes <= target,
            $"сумма {result.ResultBytes} должна быть не больше предела {target}");
        Assert.Equal(3, result.Files.Count);

        // Важно: предел — на группу, значит на диске должна лежать именно эта сумма.
        long onDisk = result.Files.Sum(f => new FileInfo(f.OutputPath!).Length);
        Assert.Equal(result.ResultBytes, onDisk);
    }

    [Fact]
    public void Группа_ФайлыОстаютсяЧитаемымиPdf()
    {
        var jobs = ThreeScans();
        var result = _service.CompressGroupToTargetSize(jobs, TotalOf(jobs) / 3);

        foreach (var file in result.Files)
        {
            using var document = PdfReader.Open(file.OutputPath!, PdfDocumentOpenMode.Import);
            Assert.Equal(1, document.PageCount);
        }
    }

    [Fact]
    public void Группа_НедостижимыйПредел_СообщаетИВсёРавноПишет()
    {
        var jobs = ThreeScans();

        var result = _service.CompressGroupToTargetSize(jobs, 2048);

        Assert.False(result.TargetReached);
        Assert.All(result.Files, f => Assert.True(File.Exists(f.OutputPath!),
            "лучший достигнутый вариант всё равно записывается"));
    }

    [Fact]
    public void Группа_УжеМеньшеПредела_КопируетБезИзменений()
    {
        var jobs = new List<CompressionJob>
        {
            Job("вектор1.pdf", TestPdfBuilder.VectorOnly()),
            Job("вектор2.pdf", TestPdfBuilder.VectorOnly()),
        };
        long original = TotalOf(jobs);

        var result = _service.CompressGroupToTargetSize(jobs, original * 10);

        Assert.True(result.TargetReached);
        Assert.Equal(original, result.ResultBytes);
        Assert.All(result.Files, f => Assert.Equal(CompressionOutcome.AlreadySmallEnough, f.Outcome));

        foreach (var job in jobs)
            Assert.Equal(File.ReadAllBytes(job.SourcePath), File.ReadAllBytes(job.OutputPath));
    }

    [Fact]
    public void Группа_ЧемЖёстчеПредел_ТемМеньшеРезультат()
    {
        long original = TotalOf(ThreeScans());

        long Size(long target)
        {
            // Для каждого замера свои файлы: выходные пути не должны пересекаться.
            var jobs = new List<CompressionJob>
            {
                Job($"{target}-один.pdf", TestPdfBuilder.WithPhotoPages()),
                Job($"{target}-два.pdf", TestPdfBuilder.WithPhotoPages()),
            };
            return _service.CompressGroupToTargetSize(jobs, target).ResultBytes;
        }

        long loose = Size(original);          // мягкий предел
        long tight = Size(original / 6);      // жёсткий

        Assert.True(tight < loose, $"жёсткий предел ({tight}) должен дать меньше мягкого ({loose})");
    }

    [Fact]
    public void Группа_ПустойСписок_НеПадает()
    {
        var result = _service.CompressGroupToTargetSize(Array.Empty<CompressionJob>(), 1024);

        Assert.True(result.TargetReached);
        Assert.Empty(result.Files);
        Assert.Equal(0, result.ResultBytes);
    }

    [Fact]
    public void Группа_БитыйФайлНеРоняетОстальные()
    {
        var jobs = ThreeScans();
        jobs.Add(Job("битый.pdf", "это вовсе не PDF"u8.ToArray()));

        var result = _service.CompressGroupToTargetSize(jobs, TotalOf(jobs) / 2);

        Assert.Equal(4, result.Files.Count);
        Assert.Single(result.Files, f => f.Outcome == CompressionOutcome.Failed);
        Assert.Equal(3, result.Files.Count(f => f.Outcome != CompressionOutcome.Failed));
    }

    [Fact]
    public void Группа_НулевойПредел_Отвергается()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _service.CompressGroupToTargetSize(ThreeScans(), 0));
    }

    [Fact]
    public void ПутьРезультата_СохраняетСтруктуруПодпапок()
    {
        var entry = new PdfFileEntry(
            @"C:\дела\А\январь\акт.pdf", @"C:\дела\А", Path.Combine("январь", "акт.pdf"), 100);

        string output = PdfCompressionService.BuildOutputPath(entry, @"C:\готово");

        Assert.Equal(Path.Combine(@"C:\готово", "январь", "акт.pdf"), output);
    }
}
