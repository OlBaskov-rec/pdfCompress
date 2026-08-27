namespace PdfCompress.Core.Models;

/// <summary>
/// Два взаимоисключающих способа задать сжатие — ровно те, что видит пользователь
/// в двух переключателях главного окна.
/// </summary>
public enum CompressionMode
{
    /// <summary>Фиксированная степень сжатия (1..5).</summary>
    Level,

    /// <summary>Подбор параметров под заданный максимальный размер файла.</summary>
    TargetSize,
}

/// <summary>Что именно требуется сделать с файлами — задание на обработку.</summary>
public sealed record CompressionRequest
{
    public required CompressionMode Mode { get; init; }

    /// <summary>Степень сжатия; используется только при <see cref="CompressionMode.Level"/>.</summary>
    public CompressionLevel Level { get; init; } = CompressionLevel.Medium;

    /// <summary>Предельный размер результата в байтах; только при <see cref="CompressionMode.TargetSize"/>.</summary>
    public long TargetBytes { get; init; }

    /// <summary>Задание для фиксированной степени сжатия.</summary>
    public static CompressionRequest ForLevel(CompressionLevel level) =>
        new() { Mode = CompressionMode.Level, Level = level };

    /// <summary>Задание для подбора под размер.</summary>
    public static CompressionRequest ForTargetSize(long bytes) =>
        new() { Mode = CompressionMode.TargetSize, TargetBytes = bytes };
}
