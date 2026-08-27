using System.Globalization;
using PdfCompress.Core.Models;
using Xunit;

namespace PdfCompress.Core.Tests;

public class SizeUnitsTests
{
    [Theory]
    [InlineData(1, SizeUnit.Bytes, 1L)]
    [InlineData(1, SizeUnit.Kilobytes, 1024L)]
    [InlineData(2.5, SizeUnit.Megabytes, 2621440L)]
    [InlineData(1, SizeUnit.Gigabytes, 1073741824L)]
    public void TryToBytes_ПереводитЗначениеВБайты(double value, SizeUnit unit, long expected)
    {
        Assert.True(SizeUnits.TryToBytes(value, unit, out long bytes));
        Assert.Equal(expected, bytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    public void TryToBytes_ОтклоняетНекорректныйРазмер(double value)
    {
        Assert.False(SizeUnits.TryToBytes(value, SizeUnit.Megabytes, out long bytes));
        Assert.Equal(0, bytes);
    }

    [Fact]
    public void TryToBytes_ОтклоняетПереполнение()
    {
        Assert.False(SizeUnits.TryToBytes(1e12, SizeUnit.Gigabytes, out _));
    }

    [Theory]
    [InlineData(512, "512 Б")]
    [InlineData(1024, "1 КБ")]
    [InlineData(1536, "1,5 КБ")]
    [InlineData(5 * 1024 * 1024, "5 МБ")]
    public void Format_ДаётКороткуюПодпись(long bytes, string expected)
    {
        Assert.Equal(expected, SizeUnits.Format(bytes, CultureInfo.GetCultureInfo("ru-RU")));
    }
}
