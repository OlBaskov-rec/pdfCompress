using PdfCompress.Core.Services;
using Xunit;

namespace PdfCompress.Core.Tests;

public class PdfFileScannerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pdfscan-tests-" + Guid.NewGuid().ToString("N"));
    private readonly PdfFileScanner _scanner = new();

    public PdfFileScannerTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* уборка не критична */ }
    }

    private void Touch(string relativePath, int size = 16)
    {
        string path = Path.Combine(_folder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
    }

    [Fact]
    public void Scan_ВозвращаетТолькоPdfПоАлфавиту()
    {
        Touch("вторая.pdf");
        Touch("Первая.pdf");
        Touch("заметка.txt");
        Touch("архив.zip");

        var found = _scanner.Scan(_folder);

        Assert.Equal(new[] { "вторая.pdf", "Первая.pdf" }, found.Select(f => f.FileName).ToArray());
    }

    [Fact]
    public void Scan_УчитываетРасширениеВЛюбомРегистре()
    {
        Touch("СКАН.PDF");

        var found = _scanner.Scan(_folder);

        Assert.Single(found);
        Assert.Equal("СКАН.PDF", found[0].FileName);
    }

    [Fact]
    public void Scan_БезРекурсииНеЗаглядываетВоВложенныеПапки()
    {
        Touch("верх.pdf");
        Touch(Path.Combine("вложенная", "низ.pdf"));

        Assert.Single(_scanner.Scan(_folder));
        Assert.Equal(2, _scanner.Scan(_folder, recursive: true).Count);
    }

    [Fact]
    public void Scan_ЗаполняетРазмерФайла()
    {
        Touch("документ.pdf", size: 2048);

        var entry = Assert.Single(_scanner.Scan(_folder));

        Assert.Equal(2048, entry.SizeBytes);
        Assert.Equal("2 КБ", entry.SizeText);
    }

    [Fact]
    public void Scan_НесуществующаяПапка_Бросает()
    {
        Assert.Throws<DirectoryNotFoundException>(() => _scanner.Scan(Path.Combine(_folder, "нет-такой")));
    }

    [Fact]
    public void Scan_ЗаполняетКореньИОтносительныйПуть()
    {
        Touch(Path.Combine("январь", "акты", "один.pdf"));

        var entry = Assert.Single(_scanner.Scan(_folder, recursive: true));

        Assert.Equal(Path.TrimEndingDirectorySeparator(_folder), entry.RootFolder);
        Assert.Equal(Path.Combine("январь", "акты", "один.pdf"), entry.RelativePath);
        Assert.Equal("январь", entry.TopLevelFolder);
        Assert.Equal("один.pdf", entry.FileName);
    }

    [Fact]
    public void ScanMany_СобираетФайлыИзНесколькихПапок()
    {
        Touch(Path.Combine("первая", "а.pdf"));
        Touch(Path.Combine("вторая", "б.pdf"));

        var found = _scanner.ScanMany(
            new[] { Path.Combine(_folder, "первая"), Path.Combine(_folder, "вторая") },
            recursive: false,
            out var skipped);

        Assert.Equal(2, found.Count);
        Assert.Empty(skipped);
    }

    [Fact]
    public void ScanMany_ВложенныеКорни_ФайлНеДублируется()
    {
        Touch(Path.Combine("внешняя", "внутренняя", "один.pdf"));

        // Одна папка вложена в другую: с рекурсией файл виден из обеих.
        var found = _scanner.ScanMany(
            new[] { Path.Combine(_folder, "внешняя"), Path.Combine(_folder, "внешняя", "внутренняя") },
            recursive: true,
            out _);

        Assert.Single(found);
    }

    [Fact]
    public void ScanMany_НедоступнаяПапкаНеРоняетОстальные()
    {
        Touch("есть.pdf");

        var found = _scanner.ScanMany(
            new[] { _folder, Path.Combine(_folder, "нет-такой") },
            recursive: false,
            out var skipped);

        Assert.Single(found);
        Assert.Single(skipped);
    }
}
