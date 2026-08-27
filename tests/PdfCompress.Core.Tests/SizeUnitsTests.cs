using System.Globalization;
using PdfCompress.Core.Models;
using Xunit;

namespace PdfCompress.Core.Tests;

public class SizeUnitsTests
{
    // Счёт десятичный: «1 МБ» = 1 000 000 Б. Это самое строгое прочтение — файл, уложившийся
    // в такой предел, уложится и в двоичный, а наоборот бывает и не так.
    [Theory]
    [InlineData(1, SizeUnit.Bytes, 1L)]
    [InlineData(1, SizeUnit.Kilobytes, 1_000L)]
    [InlineData(2.5, SizeUnit.Megabytes, 2_500_000L)]
    [InlineData(1, SizeUnit.Gigabytes, 1_000_000_000L)]
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
    [InlineData(1_000, "1 КБ")]
    [InlineData(1_500, "1,5 КБ")]
    [InlineData(5_000_000, "5 МБ")]
    [InlineData(1_048_478, "1 МБ")] // «почти мегабайт» по-двоичному — на деле уже больше него
    public void Format_ДаётКороткуюПодпись(long bytes, string expected)
    {
        Assert.Equal(expected, SizeUnits.Format(bytes, CultureInfo.GetCultureInfo("ru-RU")));
    }
}
