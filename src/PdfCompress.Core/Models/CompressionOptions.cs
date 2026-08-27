namespace PdfCompress.Core.Models;

/// <summary>
/// Параметры одного прохода сжатия. Это «внутренняя» настройка движка: степени сжатия
/// (<see cref="CompressionLevel"/>) и подбор под целевой размер разворачиваются именно в неё.
/// </summary>
public sealed record CompressionOptions
{
    /// <summary>Целевое разрешение растров в точках на дюйм от их РЕАЛЬНОГО размера на странице.</summary>
    public required int TargetDpi { get; init; }

    /// <summary>Качество JPEG (1..100) для цветных и полутоновых изображений.</summary>
    public required int JpegQuality { get; init; }

    /// <summary>
    /// Изображения мельче этого числа пикселей не трогаем: иконки и логотипы от пересжатия
    /// только теряют вид, а выигрыш в байтах ничтожен.
    /// </summary>
    public int MinImagePixels { get; init; } = 8_000;

    /// <summary>
    /// Заменяем поток только если новый вариант меньше исходного хотя бы во столько раз.
    /// Экономия в пару процентов не стоит потери качества.
    /// </summary>
    public double MaxAcceptedSizeRatio { get; init; } = 0.9;

    /// <summary>Профиль для заданной степени сжатия.</summary>
    public static CompressionOptions ForLevel(CompressionLevel level) => level switch
    {
        CompressionLevel.Minimal => new CompressionOptions { TargetDpi = 300, JpegQuality = 92 },
        CompressionLevel.Light   => new CompressionOptions { TargetDpi = 220, JpegQuality = 85 },
        CompressionLevel.Medium  => new CompressionOptions { TargetDpi = 150, JpegQuality = 75 },
        CompressionLevel.Strong  => new CompressionOptions { TargetDpi = 110, JpegQuality = 60 },
        CompressionLevel.Maximum => new CompressionOptions { TargetDpi = 72,  JpegQuality = 40 },
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Неизвестная степень сжатия."),
    };

    /// <summary>
    /// Профиль по непрерывной «силе» 0..1 (0 — мягче минимального, 1 — жёстче максимального).
    /// Нужен для подбора под целевой размер: движок ищет по этой шкале двоичным поиском.
    /// </summary>
    public static CompressionOptions ForStrength(double strength)
    {
        double s = Math.Clamp(strength, 0, 1);
        // Разрешение сокращаем геометрически (визуально шкала так воспринимается ровнее),
        // качество — линейно.
        int dpi = (int)Math.Round(400 * Math.Pow(40.0 / 400.0, s));
        int quality = (int)Math.Round(95 - 70 * s);
        return new CompressionOptions
        {
            TargetDpi = Math.Clamp(dpi, 30, 400),
            JpegQuality = Math.Clamp(quality, 20, 95),
        };
    }
}
