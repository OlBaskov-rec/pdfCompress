using System.Runtime.InteropServices;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using SkiaSharp;

namespace PdfCompress.Core.Services;

/// <summary>
/// Собирает <see cref="SKBitmap"/> из НЕ-JPEG растра PDF (Flate/LZW/RunLength — «сырые» отсчёты).
///
/// Сознательно поддерживаются только 8-битные DeviceGray / DeviceRGB / DeviceCMYK (и их
/// ICCBased/Cal-эквиваленты) — это подавляющее большинство «тяжёлых» картинок. Индексированные,
/// однобитные и штриховые (CCITT, JBIG2) изображения не трогаются намеренно: они и так занимают
/// мало места, а перевод их в JPEG испортил бы вид сильнее, чем сэкономил байты.
/// </summary>
internal static class PdfRawImageDecoder
{
    private enum ColorKind { Gray, Rgb, Cmyk }

    /// <summary>Разворачивает косвенную ссылку в сам объект.</summary>
    public static PdfItem? Resolve(PdfItem? item) =>
        item is PdfReference reference ? reference.Value : item;

    /// <summary>
    /// Полутоновое ли изображение по данным самого PDF. Такие растры выгодно и кодировать
    /// в серый JPEG: файл заметно меньше, а цветной каймы по краям букв не появляется.
    /// </summary>
    public static bool IsGrayscale(PdfDictionary image) =>
        TryGetColorKind(image, out var kind) && kind == ColorKind.Gray;

    /// <summary>
    /// Декодирует растр в 32-битный BGRA-битмап. Возвращает null, если формат не поддержан
    /// или данных меньше, чем обещают /Width и /Height.
    /// </summary>
    /// <param name="samples">
    /// Уже освобождённые от фильтров отсчёты (см. <see cref="PdfStreamFilters.TryUnwrap"/>).
    /// </param>
    public static SKBitmap? TryDecode(PdfDictionary image, byte[] samples, int width, int height)
    {
        int bpc = image.Elements.GetInteger("/BitsPerComponent");
        if (bpc != 8)
            return null;

        if (!TryGetColorKind(image, out var kind))
            return null;

        int components = kind switch { ColorKind.Gray => 1, ColorKind.Rgb => 3, _ => 4 };
        long needed = (long)width * height * components;
        if (samples.LongLength < needed)
            return null;

        byte[] bgra = ToBgra(samples, width, height, kind);

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var bitmap = new SKBitmap(info);
        try
        {
            // Свежесозданный SKBitmap выделен плотно (RowBytes == width * 4), поэтому копия
            // одним блоком корректна и не требует unsafe-кода.
            Marshal.Copy(bgra, 0, bitmap.GetPixels(), bgra.Length);
            return bitmap;
        }
        catch (Exception)
        {
            bitmap.Dispose();
            return null;
        }
    }

    /// <summary>Разворачивает отсчёты PDF в плотный BGRA-буфер.</summary>
    private static byte[] ToBgra(byte[] samples, int width, int height, ColorKind kind)
    {
        var bgra = new byte[(long)width * height * 4];

        int src = 0, dst = 0;
        for (int i = 0, total = width * height; i < total; i++)
        {
            byte b, g, r;
            switch (kind)
            {
                case ColorKind.Gray:
                    b = g = r = samples[src];
                    src += 1;
                    break;

                case ColorKind.Rgb:
                    r = samples[src];
                    g = samples[src + 1];
                    b = samples[src + 2];
                    src += 3;
                    break;

                default: // CMYK: значения PDF — «сколько краски», 0 = нет краски.
                    int c = samples[src], m = samples[src + 1], y = samples[src + 2], k = samples[src + 3];
                    src += 4;
                    r = (byte)(255 - Math.Min(255, c + k));
                    g = (byte)(255 - Math.Min(255, m + k));
                    b = (byte)(255 - Math.Min(255, y + k));
                    break;
            }

            bgra[dst] = b;
            bgra[dst + 1] = g;
            bgra[dst + 2] = r;
            bgra[dst + 3] = 255;
            dst += 4;
        }

        return bgra;
    }

    private static bool TryGetColorKind(PdfDictionary image, out ColorKind kind)
    {
        kind = ColorKind.Gray;
        var cs = Resolve(image.Elements.GetValue("/ColorSpace"));

        if (cs is PdfName name)
            return TryFromName(name.Value, out kind);

        if (cs is PdfArray array && array.Elements.Count > 0)
        {
            string family = array.Elements.GetName(0);
            switch (family)
            {
                case "/CalGray":
                    kind = ColorKind.Gray;
                    return true;

                case "/CalRGB":
                    kind = ColorKind.Rgb;
                    return true;

                case "/ICCBased":
                    // Число компонент профиля лежит в /N словаря потока — по нему и определяем модель.
                    var profile = array.Elements.GetDictionary(1);
                    return profile is not null && TryFromComponents(profile.Elements.GetInteger("/N"), out kind);
            }
        }

        return false;
    }

    private static bool TryFromName(string name, out ColorKind kind)
    {
        switch (name)
        {
            case "/DeviceGray" or "/G" or "/CalGray":
                kind = ColorKind.Gray;
                return true;
            case "/DeviceRGB" or "/RGB" or "/CalRGB":
                kind = ColorKind.Rgb;
                return true;
            case "/DeviceCMYK" or "/CMYK":
                kind = ColorKind.Cmyk;
                return true;
            default:
                kind = ColorKind.Gray;
                return false;
        }
    }

    private static bool TryFromComponents(int n, out ColorKind kind)
    {
        switch (n)
        {
            case 1: kind = ColorKind.Gray; return true;
            case 3: kind = ColorKind.Rgb; return true;
            case 4: kind = ColorKind.Cmyk; return true;
            default: kind = ColorKind.Gray; return false;
        }
    }
}
