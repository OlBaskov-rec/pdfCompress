using PdfCompress.Core.Models;
using PdfCompress.Core.Services;
using Xunit;

namespace PdfCompress.Core.Tests;

public class FileGroupingTests
{
    private const string RootA = @"C:\дела\А";
    private const string RootB = @"C:\дела\Б";

    private static PdfFileEntry Entry(string root, string relative, long size = 1000) =>
        new(Path.Combine(root, relative), root, relative, size);

    [Fact]
    public void ПоФайлам_КаждыйФайлОтдельнаяГруппа()
    {
        var files = new[]
        {
            Entry(RootA, "первый.pdf"),
            Entry(RootA, Path.Combine("вложенная", "второй.pdf")),
        };

        var groups = FileGrouping.Build(files, TargetScope.File);

        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Single(g.Files));
    }

    [Fact]
    public void ПоПапке_ВсёИзОдногоКорняВОднойГруппе()
    {
        var files = new[]
        {
            Entry(RootA, "первый.pdf"),
            Entry(RootA, Path.Combine("вложенная", "второй.pdf")),
            Entry(RootB, "третий.pdf"),
        };

        var groups = FileGrouping.Build(files, TargetScope.Folder);

        Assert.Equal(2, groups.Count);
        Assert.Equal(2, groups[0].Files.Count);
        Assert.Single(groups[1].Files);
        Assert.Equal("А", groups[0].Title);
        Assert.Equal("Б", groups[1].Title);
    }

    [Fact]
    public void ПоПодпапкам_ГруппируетПоПервомуУровню()
    {
        var files = new[]
        {
            Entry(RootA, "в корне.pdf"),
            Entry(RootA, Path.Combine("январь", "один.pdf")),
            Entry(RootA, Path.Combine("январь", "два.pdf")),
            Entry(RootA, Path.Combine("февраль", "три.pdf")),
            // Глубокая вложенность всё равно относится к подпапке первого уровня.
            Entry(RootA, Path.Combine("январь", "акты", "четыре.pdf")),
        };

        var groups = FileGrouping.Build(files, TargetScope.Subfolder);

        Assert.Equal(3, groups.Count);
        Assert.Equal("А (файлы в корне)", groups[0].Title);
        Assert.Single(groups[0].Files);

        Assert.Equal(@"А\январь", groups[1].Title);
        Assert.Equal(3, groups[1].Files.Count);

        Assert.Equal(@"А\февраль", groups[2].Title);
        Assert.Single(groups[2].Files);
    }

    [Fact]
    public void ПоПодпапкам_ОдноимённыеПодпапкиРазныхКорнейНеСмешиваются()
    {
        var files = new[]
        {
            Entry(RootA, Path.Combine("январь", "один.pdf")),
            Entry(RootB, Path.Combine("январь", "два.pdf")),
        };

        var groups = FileGrouping.Build(files, TargetScope.Subfolder);

        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Single(g.Files));
    }

    [Fact]
    public void СуммаГруппы_СкладываетРазмерыФайлов()
    {
        var files = new[]
        {
            Entry(RootA, "один.pdf", 1500),
            Entry(RootA, "два.pdf", 2500),
        };

        var group = Assert.Single(FileGrouping.Build(files, TargetScope.Folder));

        Assert.Equal(4000, group.TotalBytes);
    }

    [Fact]
    public void ВерхняяПапка_ДляФайлаВКорнеПуста()
    {
        Assert.Equal(string.Empty, Entry(RootA, "один.pdf").TopLevelFolder);
        Assert.Equal("январь", Entry(RootA, Path.Combine("январь", "один.pdf")).TopLevelFolder);
    }
}
