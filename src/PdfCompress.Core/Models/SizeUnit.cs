using System.Globalization;

namespace PdfCompress.Core.Models;

/// <summary>Единица измерения размера файла (для режима «максимальный размер»).</summary>
public enum SizeUnit
{
    Bytes = 0,
    Kilobytes = 1,
    Megabytes = 2,
    Gigabytes = 3,
}

/// <summary>Перевод «число + единица» в байты и обратно, плюс человекочитаемый формат.</summary>
public static class SizeUnits
{
    /// <summary>Множитель единицы в байтах (двоичные килобайты: 1 КБ = 1024 Б).</summary>
    public static long Multiplier(SizeUnit unit) => unit switch
    {
        SizeUnit.Bytes => 1L,
        SizeUnit.Kilobytes => 1024L,
        SizeUnit.Megabytes => 1024L * 1024,
        SizeUnit.Gigabytes => 1024L * 1024 * 1024,
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Неизвестная единица измерения."),
    };

    /// <summary>
    /// Переводит введённое пользователем значение в байты. Возвращает false при нуле,
    /// отрицательном значении или переполнении (например, 999999 ГБ).
    /// </summary>
    public static bool TryToBytes(double value, SizeUnit unit, out long bytes)
    {
        bytes = 0;
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            return false;

        double raw = value * Multiplier(unit);
        if (raw >= long.MaxValue)
            return false;

        bytes = (long)Math.Round(raw);
        return bytes > 0;
    }

    /// <summary>Размер в байтах — в короткую подпись вида «12,4 МБ».</summary>
    public static string Format(long bytes, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        string[] names = { "Б", "КБ", "МБ", "ГБ", "ТБ" };

        double value = bytes;
        int i = 0;
        while (value >= 1024 && i < names.Length - 1)
        {
            value /= 1024;
            i++;
        }

        // Байты — всегда целые; для остальных единиц одного знака после запятой достаточно.
        string number = i == 0
            ? value.ToString("0", culture)
            : value.ToString("0.#", culture);
        return $"{number} {names[i]}";
    }
}
