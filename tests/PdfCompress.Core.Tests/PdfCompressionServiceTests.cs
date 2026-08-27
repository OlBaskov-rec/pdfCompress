using PdfCompress.Core.Models;
using PdfCompress.Core.Services;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PdfCompress.Core.Tests;

public class PdfCompressionServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pdfcompress-tests-" + Guid.NewGuid().ToString("N"));
    private readonly PdfCompressionService _service = new();

    public PdfCompressionServiceTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* уборка не критична */ }
    }

    private string WriteSource(byte[] bytes, string name = "source.pdf")
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string OutputPath(string name = "result.pdf") => Path.Combine(_folder, "out", name);

    [Fact]
    public void Compress_ПоСтепени_УменьшаетФайлИСохраняетСтраницы()
    {
        string source = WriteSource(TestPdfBuilder.WithPhotoPages(pageCount: 2));
        string output = OutputPath();

        var result = _service.Compress(source, output, CompressionRequest.ForLevel(CompressionLevel.Medium));

        Assert.Equal(CompressionOutcome.Compressed, result.Outcome);
        Assert.True(result.ResultBytes < result.OriginalBytes,
            $"ожидалось уменьшение: было {result.OriginalBytes}, стало {result.ResultBytes}");
        Assert.True(result.ImagesRecompressed > 0, "фотографии должны были попасть под пересжатие");

        using var compressed = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        Assert.Equal(2, compressed.PageCount);
    }

    [Fact]
    public void Compress_ЧемВышеСтепеньТемМеньшеРезультат()
    {
        string source = WriteSource(TestPdfBuilder.WithPhotoPages());

        long Size(CompressionLevel level)
        {
            string output = OutputPath($"{level}.pdf");
            return _service.Compress(source, output, CompressionRequest.ForLevel(level)).ResultBytes;
        }

        long minimal = Size(CompressionLevel.Minimal);
        long medium = Size(CompressionLevel.Medium);
        long maximum = Size(CompressionLevel.Maximum);

        Assert.True(medium < minimal, $"среднее ({medium}) должно быть меньше минимального ({minimal})");
        Assert.True(maximum < medium, $"максимальное ({maximum}) должно быть меньше среднего ({medium})");
    }

    [Fact]
    public void Compress_РастрВЦепочкеФильтров_ВсёРавноПересжимается()
    {
        // Сканеры часто пишут JPEG, обёрнутый ещё и во Flate: /Filter [/FlateDecode /DCTDecode].
        // Пока цепочка не разбиралась, такие документы «сжимались» на 0 % — молча и без ошибки.
        string source = WriteSource(TestPdfBuilder.WithFlateWrappedJpegPage());
        string output = OutputPath();

        var result = _service.Compress(source, output, CompressionRequest.ForLevel(CompressionLevel.Medium));

        Assert.Equal(CompressionOutcome.Compressed, result.Outcome);
        Assert.True(result.ImagesRecompressed > 0,
            "растр под цепочкой фильтров обязан попасть под пересжатие");
        Assert.True(result.ResultBytes < result.OriginalBytes,
            $"ожидалось уменьшение: было {result.OriginalBytes}, стало {result.ResultBytes}");

        using var compressed = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        Assert.Equal(1, compressed.PageCount);
    }

    [Fact]
    public void Compress_ПоРазмеру_УкладываетсяВЗаданныйПредел()
    {
        string source = WriteSource(TestPdfBuilder.WithPhotoPages(pageCount: 3));
        long original = new FileInfo(source).Length;
        long target = original / 5;
        string output = OutputPath();

        var result = _service.Compress(source, output, CompressionRequest.ForTargetSize(target));

        Assert.Equal(CompressionOutcome.Compressed, result.Outcome);
        Assert.True(result.ResultBytes <= target,
            $"результат {result.ResultBytes} должен уложиться в предел {target}");
        Assert.True(result.Attempts > 1, "подбор под размер делается за несколько проходов");
        Assert.Equal(result.ResultBytes, new FileInfo(output).Length);
    }

    [Fact]
    public void Compress_ПоРазмеру_ФайлУжеМеньшеПредела_КопируетБезИзменений()
    {
        string source = WriteSource(TestPdfBuilder.VectorOnly());
        long original = new FileInfo(source).Length;
        string output = OutputPath();

        var result = _service.Compress(source, output, CompressionRequest.ForTargetSize(original * 10));

        Assert.Equal(CompressionOutcome.AlreadySmallEnough, result.Outcome);
        Assert.Equal(original, result.ResultBytes);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(output));
    }

    [Fact]
    public void Compress_ПоРазмеру_НедостижимыйПредел_СообщаетОНедостижении()
    {
        string source = WriteSource(TestPdfBuilder.WithPhotoPages());
        string output = OutputPath();

        var result = _service.Compress(source, output, CompressionRequest.ForTargetSize(1024));

        Assert.Equal(CompressionOutcome.TargetNotReached, result.Outcome);
        Assert.True(File.Exists(output), "лучший достигнутый вариант всё равно записывается");
    }

    [Fact]
    public void Compress_ЗаписьНаМестоИсходника_НеПортитФайл()
    {
        string source = WriteSource(TestPdfBuilder.WithPhotoPages());
        long original = new FileInfo(source).Length;

        var result = _service.Compress(source, source, CompressionRequest.ForLevel(CompressionLevel.Strong));

        Assert.Equal(CompressionOutcome.Compressed, result.Outcome);
        Assert.True(new FileInfo(source).Length < original);

        using var compressed = PdfReader.Open(source, PdfDocumentOpenMode.Import);
        Assert.Equal(1, compressed.PageCount);
    }

    [Fact]
    public void Compress_НеPdfФайл_ВозвращаетОшибкуБезИсключения()
    {
        string source = Path.Combine(_folder, "broken.pdf");
        File.WriteAllText(source, "это вовсе не PDF");
        string output = OutputPath();

        var result = _service.Compress(source, output, CompressionRequest.ForLevel(CompressionLevel.Medium));

        Assert.Equal(CompressionOutcome.Failed, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.False(File.Exists(output), "битый файл не должен попадать в выходную папку");
    }

    [Fact]
    public void BuildOutputPath_КладётФайлПодТемЖеИменем()
    {
        string path = PdfCompressionService.BuildOutputPath(@"C:\docs\отчёт.pdf", @"C:\docs\compressed");
        Assert.Equal(Path.Combine(@"C:\docs\compressed", "отчёт.pdf"), path);
    }
}
