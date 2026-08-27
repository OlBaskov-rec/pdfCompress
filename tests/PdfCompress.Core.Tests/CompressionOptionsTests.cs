using PdfCompress.Core.Models;
using Xunit;

namespace PdfCompress.Core.Tests;

public class CompressionOptionsTests
{
    [Fact]
    public void ForLevel_ЧемВышеСтепеньТемНижеРазрешениеИКачество()
    {
        var levels = new[]
        {
            CompressionLevel.Minimal, CompressionLevel.Light, CompressionLevel.Medium,
            CompressionLevel.Strong, CompressionLevel.Maximum,
        };

        var options = levels.Select(CompressionOptions.ForLevel).ToArray();

        for (int i = 1; i < options.Length; i++)
        {
            Assert.True(options[i].TargetDpi < options[i - 1].TargetDpi,
                $"dpi должен убывать: {levels[i]} против {levels[i - 1]}");
            Assert.True(options[i].JpegQuality < options[i - 1].JpegQuality,
                $"качество должно убывать: {levels[i]} против {levels[i - 1]}");
        }
    }

    [Fact]
    public void ForStrength_МонотоннаИНеВыходитЗаГраницы()
    {
        var gentle = CompressionOptions.ForStrength(0);
        var middle = CompressionOptions.ForStrength(0.5);
        var harsh = CompressionOptions.ForStrength(1);

        Assert.True(gentle.TargetDpi > middle.TargetDpi && middle.TargetDpi > harsh.TargetDpi);
        Assert.True(gentle.JpegQuality > middle.JpegQuality && middle.JpegQuality > harsh.JpegQuality);
        Assert.InRange(harsh.JpegQuality, 20, 95);
        Assert.InRange(gentle.TargetDpi, 30, 400);
    }

    [Fact]
    public void ForStrength_ЗажимаетЗначенияЗаПределамиШкалы()
    {
        Assert.Equal(CompressionOptions.ForStrength(0).TargetDpi, CompressionOptions.ForStrength(-3).TargetDpi);
        Assert.Equal(CompressionOptions.ForStrength(1).TargetDpi, CompressionOptions.ForStrength(42).TargetDpi);
    }
}
