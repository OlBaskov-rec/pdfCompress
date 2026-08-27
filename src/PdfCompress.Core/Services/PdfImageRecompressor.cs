using System.IO.Compression;
using PdfCompress.Core.Models;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using SkiaSharp;

namespace PdfCompress.Core.Services;

/// <summary>
/// Уменьшает растры внутри документа — главный источник экономии в «тяжёлых» PDF.
///
/// Для каждого изображения считается, с каким разрешением оно реально печатается на странице
/// (см. <see cref="ImagePlacementAnalyzer"/>); всё, что выше заданного dpi, масштабируется вниз
/// и перекодируется в JPEG. Новый поток ставится на место старого только если он ЗАМЕТНО меньше,
/// поэтому «уже сжатые» документы не портятся ради пары процентов.
///
/// Маски прозрачности (/SMask) обрабатываются отдельно и всегда без потерь (Flate): JPEG на
/// альфа-канале даёт заметные ореолы по краям.
/// </summary>
public sealed class PdfImageRecompressor
{
    /// <summary>Ниже этого размера перекодировать нечего — накладные расходы съедят выигрыш.</summary>
    private const int MinSideForResize = 8;

    /// <summary>
    /// Обрабатывает все растры документа. Возвращает число реально заменённых изображений.
    /// Изображение, которое не удалось разобрать, молча остаётся в исходном виде.
    /// </summary>
    public int Process(PdfDocument document, CompressionOptions options, CancellationToken cancellationToken = default)
    {
        var placements = ImagePlacementAnalyzer.Analyze(document);
        var images = CollectImages(document);
        var softMasks = CollectSoftMaskIds(images);

        int replaced = 0;
        foreach (var image in images)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var imageId = PdfObjectIds.Of(image);
            placements.TryGetValue(imageId, out var placement);
            bool isMask = !imageId.IsEmpty && softMasks.Contains(imageId);

            try
            {
                if (TryRecompress(image, placement, options, isMask))
                    replaced++;
            }
            catch (Exception)
            {
                // Экзотический растр (нестандартный фильтр, битые данные) — оставляем как есть.
                // Один плохой объект не должен ронять обработку всего документа.
            }
        }

        return replaced;
    }

    private static List<PdfDictionary> CollectImages(PdfDocument document)
    {
        var result = new List<PdfDictionary>();
        foreach (var obj in document.Internals.GetAllObjects())
        {
            if (obj is PdfDictionary dict
                && dict.Stream is not null
                && dict.Elements.GetName("/Subtype") == "/Image")
            {
                result.Add(dict);
            }
        }
        return result;
    }

    /// <summary>Объекты, на которые ссылаются как на маску прозрачности другого изображения.</summary>
    private static HashSet<PdfObjectID> CollectSoftMaskIds(List<PdfDictionary> images)
    {
        var ids = new HashSet<PdfObjectID>();
        foreach (var image in images)
        {
            if (image.Elements.GetValue("/SMask") is PdfReference smask)
                ids.Add(smask.ObjectID);
            if (image.Elements.GetValue("/Mask") is PdfReference mask)
                ids.Add(mask.ObjectID);
        }
        return ids;
    }

    private bool TryRecompress(PdfDictionary image, ImagePlacement? placement, CompressionOptions options, bool isSoftMask)
    {
        if (!IsCandidate(image, options, out int width, out int height))
            return false;

        var (targetWidth, targetHeight) = TargetSize(width, height, placement, options.TargetDpi);

        using var source = Decode(image, width, height);
        if (source is null)
            return false;

        long originalLength = image.Stream!.Value.LongLength;

        if (isSoftMask)
        {
            // Маску имеет смысл трогать, только если она реально уменьшилась в пикселях:
            // без потерь при том же размере выигрыша не будет.
            if (targetWidth == width && targetHeight == height)
                return false;

            using var mask = Resize(source, targetWidth, targetHeight, SKColorType.Bgra8888);
            if (mask is null)
                return false;

            byte[] gray = Deflate(ToGrayscale(mask));
            if (!IsWorthIt(gray.LongLength, originalLength, options))
                return false;

            Apply(image, gray, "/FlateDecode", "/DeviceGray", mask.Width, mask.Height);
            return true;
        }

        // Полутоновый оригинал кодируем в серый JPEG: канал один вместо трёх.
        bool grayscale = PdfRawImageDecoder.IsGrayscale(image);
        using var scaled = Resize(source, targetWidth, targetHeight,
            grayscale ? SKColorType.Gray8 : SKColorType.Bgra8888);
        if (scaled is null)
            return false;

        using var data = scaled.Encode(SKEncodedImageFormat.Jpeg, options.JpegQuality);
        if (data is null || data.Size == 0)
            return false;

        byte[] jpeg = data.ToArray();
        if (!IsWorthIt(jpeg.LongLength, originalLength, options))
            return false;

        Apply(image, jpeg, "/DCTDecode", grayscale ? "/DeviceGray" : "/DeviceRGB", scaled.Width, scaled.Height);
        return true;
    }

    /// <summary>Отсеивает всё, что трогать нельзя или бессмысленно, и заодно читает размеры.</summary>
    private static bool IsCandidate(PdfDictionary image, CompressionOptions options, out int width, out int height)
    {
        width = image.Elements.GetInteger("/Width");
        height = image.Elements.GetInteger("/Height");

        if (width < MinSideForResize || height < MinSideForResize)
            return false;
        if ((long)width * height < options.MinImagePixels)
            return false;

        // Штриховая маска (1 бит на пиксель): в JPEG её переводить нельзя.
        if (image.Elements.GetBoolean("/ImageMask"))
            return false;

        // Нестандартная таблица /Decode (например, инвертированный CMYK) — наш декодер её
        // не применяет, поэтому цвета поехали бы. Такие картинки не трогаем.
        if (image.Elements.ContainsKey("/Decode"))
            return false;

        // Цветовое маскирование (/Mask массивом) задано в исходном цветовом пространстве —
        // после перевода в DeviceRGB диапазоны стали бы бессмысленными.
        if (PdfRawImageDecoder.Resolve(image.Elements.GetValue("/Mask")) is PdfArray)
            return false;

        return true;
    }

    /// <summary>JPEG декодирует Skia; всё остальное собираем из сырых отсчётов сами.</summary>
    private static SKBitmap? Decode(PdfDictionary image, int width, int height)
    {
        if (FilterOf(image) == "/DCTDecode")
        {
            try
            {
                return SKBitmap.Decode(image.Stream!.Value);
            }
            catch (Exception)
            {
                return null; // CMYK-JPEG и прочая экзотика — Skia не берёт.
            }
        }

        return PdfRawImageDecoder.TryDecode(image, width, height);
    }

    /// <summary>Имя единственного фильтра потока; для цепочки фильтров — пустая строка.</summary>
    private static string FilterOf(PdfDictionary image)
    {
        var filter = PdfRawImageDecoder.Resolve(image.Elements.GetValue("/Filter"));
        return filter switch
        {
            PdfName name => name.Value,
            PdfArray array when array.Elements.Count == 1 => array.Elements.GetName(0),
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Сколько пикселей достаточно, чтобы картинка выглядела на странице с заданным dpi.
    /// Увеличение исключено: коэффициент никогда не превышает 1.
    /// </summary>
    private static (int Width, int Height) TargetSize(int width, int height, ImagePlacement? placement, int dpi)
    {
        double scale;
        if (placement is { WidthPt: > 0.5, HeightPt: > 0.5 })
        {
            double allowedWidth = placement.WidthPt / 72.0 * dpi;
            double allowedHeight = placement.HeightPt / 72.0 * dpi;
            scale = Math.Min(allowedWidth / width, allowedHeight / height);
        }
        else
        {
            // Размер вывода неизвестен (не нашли изображение в потоке содержимого) —
            // считаем по худшему случаю: картинка во весь лист A4 по длинной стороне.
            scale = dpi * 11.7 / Math.Max(width, height);
        }

        scale = Math.Min(1.0, scale);
        return (Math.Max(1, (int)Math.Round(width * scale)),
                Math.Max(1, (int)Math.Round(height * scale)));
    }

    /// <summary>
    /// Масштабирует растр и заодно переводит его в нужный формат пикселей
    /// (Gray8 для полутоновых — тогда и JPEG получится однокональным).
    /// </summary>
    private static SKBitmap? Resize(SKBitmap source, int width, int height, SKColorType colorType)
    {
        if (source.Width == width && source.Height == height && source.ColorType == colorType)
            return source.Copy();

        var info = new SKImageInfo(width, height, colorType, SKAlphaType.Opaque);
        // Mitchell — компромисс между резкостью и муаром при сильном уменьшении.
        return source.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell));
    }

    /// <summary>Заменять поток стоит, только если экономия ощутима (см. MaxAcceptedSizeRatio).</summary>
    private static bool IsWorthIt(long candidate, long original, CompressionOptions options) =>
        candidate > 0 && original > 0 && candidate <= original * options.MaxAcceptedSizeRatio;

    private static void Apply(PdfDictionary image, byte[] bytes, string filter, string colorSpace, int width, int height)
    {
        image.Stream!.Value = bytes;
        image.Elements.SetName("/Filter", filter);
        image.Elements.SetName("/ColorSpace", colorSpace);
        image.Elements.SetInteger("/Width", width);
        image.Elements.SetInteger("/Height", height);
        image.Elements.SetInteger("/BitsPerComponent", 8);
        image.Elements.SetInteger("/Length", bytes.Length);

        // Параметры старого фильтра (предиктор Flate и т. п.) к новым данным неприменимы.
        image.Elements.Remove("/DecodeParms");
        image.Elements.Remove("/F");
        image.Elements.Remove("/FFilter");
        image.Elements.Remove("/FDecodeParms");
    }

    /// <summary>Один байт серого на пиксель — формат /DeviceGray с 8 битами на компоненту.</summary>
    private static byte[] ToGrayscale(SKBitmap bitmap)
    {
        var pixels = bitmap.Pixels;
        var gray = new byte[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            var c = pixels[i];
            // Коэффициенты яркости BT.601 — стандарт для перевода RGB в серый.
            gray[i] = (byte)((c.Red * 77 + c.Green * 151 + c.Blue * 28) >> 8);
        }
        return gray;
    }

    /// <summary>Упаковка в zlib — именно этот формат ожидает фильтр /FlateDecode.</summary>
    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(data, 0, data.Length);
        return output.ToArray();
    }
}
