using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SkiaSharp;

namespace PdfCompress.Core.Tests;

/// <summary>
/// Собирает подопытные PDF-файлы: страница A4, на всю площадь которой положена «фотография».
/// Картинка шумная и с плавным градиентом — то есть сжимается ровно так же, как настоящее фото,
/// и уменьшение разрешения/качества даёт на ней измеримый эффект.
/// </summary>
internal static class TestPdfBuilder
{
    /// <summary>Ширина/высота страницы A4 в пунктах.</summary>
    private const double A4WidthPt = 595.28;
    private const double A4HeightPt = 841.89;

    /// <summary>PDF из указанного числа страниц; на каждой — фотоподобный растр заданного размера.</summary>
    public static byte[] WithPhotoPages(int pageCount = 1, int imageWidth = 1600, int imageHeight = 2200)
    {
        byte[] jpeg = PhotoJpeg(imageWidth, imageHeight);

        using var document = new PdfDocument();
        for (int i = 0; i < pageCount; i++)
        {
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(A4WidthPt);
            page.Height = XUnit.FromPoint(A4HeightPt);

            using var stream = new MemoryStream(jpeg, writable: false);
            using var image = XImage.FromStream(stream);
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawImage(image, 0, 0, A4WidthPt, A4HeightPt);
        }

        using var output = new MemoryStream();
        document.Save(output, false);
        return output.ToArray();
    }

    /// <summary>
    /// PDF без единого растра — только векторная графика. На таком файле сжимать почти нечего.
    /// Текст намеренно не используется: он потребовал бы настройки поиска шрифтов в PdfSharp,
    /// а для проверок движка достаточно линий и прямоугольников.
    /// </summary>
    public static byte[] VectorOnly(int pageCount = 1)
    {
        using var document = new PdfDocument();
        for (int i = 0; i < pageCount; i++)
        {
            var page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            for (int line = 0; line < 40; line++)
            {
                double y = 60 + line * 16;
                gfx.DrawLine(XPens.Black, 50, y, 545, y);
                gfx.DrawRectangle(XPens.DarkBlue, 50 + line * 4, y - 10, 20, 8);
            }
        }

        using var output = new MemoryStream();
        document.Save(output, false);
        return output.ToArray();
    }

    /// <summary>JPEG высокого качества: градиент плюс псевдослучайный шум.</summary>
    private static byte[] PhotoJpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        var random = new Random(20260827); // фиксированное зерно — тесты должны быть воспроизводимыми

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte r = (byte)Math.Clamp(x * 255 / width + random.Next(-25, 25), 0, 255);
                byte g = (byte)Math.Clamp(y * 255 / height + random.Next(-25, 25), 0, 255);
                byte b = (byte)Math.Clamp((x + y) * 255 / (width + height) + random.Next(-25, 25), 0, 255);
                bitmap.SetPixel(x, y, new SKColor(r, g, b));
            }
        }

        using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, 95);
        return data.ToArray();
    }
}
