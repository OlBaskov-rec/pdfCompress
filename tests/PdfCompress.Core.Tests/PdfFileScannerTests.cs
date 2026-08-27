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
}
