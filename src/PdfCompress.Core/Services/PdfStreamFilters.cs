using System.IO.Compression;
using PdfSharp.Pdf;

namespace PdfCompress.Core.Services;

/// <summary>
/// Разворачивает поток растра до «полезной нагрузки»: либо до данных картинки в её собственном
/// формате (JPEG), либо до сырых отсчётов.
///
/// В PDF <c>/Filter</c> — это ЦЕПОЧКА, а не один фильтр: сплошь и рядом встречается
/// <c>[/FlateDecode /DCTDecode]</c> — JPEG, поверх завёрнутый в Flate (так пишут многие
/// сканеры и МФУ). Пока такие цепочки не разбирались, все подобные сканы молча оставались
/// нетронутыми: именно из-за них пачка документов «сжималась» на 0 %.
/// </summary>
internal static class PdfStreamFilters
{
    /// <summary>Терминальный фильтр: дальше идут данные JPEG, а не отсчёты.</summary>
    public const string Dct = "/DCTDecode";

    /// <summary>Сырые отсчёты — терминального кодека в цепочке не было.</summary>
    public const string RawSamples = "";

    /// <summary>
    /// Снимает с потока все «транспортные» фильтры (упаковка и текстовое кодирование).
    /// </summary>
    /// <param name="terminal">
    /// <see cref="Dct"/>, если внутри JPEG, либо <see cref="RawSamples"/> для сырых отсчётов.
    /// </param>
    /// <returns>
    /// false, если в цепочке есть фильтр, который мы не разбираем (JPEG 2000, JBIG2, CCITT, LZW)
    /// или применён предиктор — такие растры трогать нельзя.
    /// </returns>
    public static bool TryUnwrap(PdfDictionary image, out byte[] payload, out string terminal)
    {
        payload = Array.Empty<byte>();
        terminal = RawSamples;

        if (image.Stream is null)
            return false;

        // Предиктор (PNG/TIFF) меняет сами отсчёты, а мы его не применяем — цвета поехали бы.
        if (HasPredictor(image))
            return false;

        var names = FilterNames(image);
        byte[] data = image.Stream.Value;

        for (int i = 0; i < names.Count; i++)
        {
            string name = names[i];

            if (name == Dct)
            {
                // JPEG обязан быть последним в цепочке; всё, что после него, — испорченный PDF.
                if (i != names.Count - 1)
                    return false;

                terminal = Dct;
                payload = data;
                return true;
            }

            byte[]? decoded = name switch
            {
                "/FlateDecode" or "/Fl" => Inflate(data),
                "/ASCII85Decode" or "/A85" => Ascii85(data),
                "/ASCIIHexDecode" or "/AHx" => AsciiHex(data),
                "/RunLengthDecode" or "/RL" => RunLength(data),
                // JPXDecode, JBIG2Decode, CCITTFaxDecode, LZWDecode и прочее — не наш случай.
                _ => null,
            };

            if (decoded is null)
                return false;

            data = decoded;
        }

        payload = data;
        return true;
    }

    /// <summary>Имена фильтров потока по порядку применения. Пустой список — фильтров нет.</summary>
    private static IReadOnlyList<string> FilterNames(PdfDictionary image)
    {
        var filter = PdfRawImageDecoder.Resolve(image.Elements.GetValue("/Filter"));
        switch (filter)
        {
            case PdfName name:
                return new[] { name.Value };

            case PdfArray array:
                var names = new List<string>(array.Elements.Count);
                for (int i = 0; i < array.Elements.Count; i++)
                    names.Add(array.Elements.GetName(i));
                return names;

            default:
                return Array.Empty<string>();
        }
    }

    /// <summary>Есть ли в параметрах фильтра предиктор (значение больше 1).</summary>
    private static bool HasPredictor(PdfDictionary image)
    {
        var parms = PdfRawImageDecoder.Resolve(image.Elements.GetValue("/DecodeParms"));

        if (parms is PdfDictionary single)
            return single.Elements.GetInteger("/Predictor") > 1;

        if (parms is PdfArray array)
        {
            for (int i = 0; i < array.Elements.Count; i++)
            {
                if (PdfRawImageDecoder.Resolve(array.Elements.GetObject(i)) is PdfDictionary d
                    && d.Elements.GetInteger("/Predictor") > 1)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Распаковка Flate. Формально это zlib, но часть генераторов пишет «голый» deflate
    /// без заголовка — поэтому при неудаче пробуем и его.
    /// </summary>
    private static byte[]? Inflate(byte[] data)
    {
        return TryInflate(data, raw: false) ?? TryInflate(data, raw: true);

        static byte[]? TryInflate(byte[] data, bool raw)
        {
            try
            {
                using var input = new MemoryStream(data, writable: false);
                using Stream decompressor = raw
                    ? new DeflateStream(input, CompressionMode.Decompress)
                    : new ZLibStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                decompressor.CopyTo(output);
                return output.Length > 0 ? output.ToArray() : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    private static byte[]? AsciiHex(byte[] data)
    {
        var output = new MemoryStream(data.Length / 2 + 1);
        int high = -1;

        foreach (byte b in data)
        {
            if (b == (byte)'>') break;          // конец данных
            int digit = HexDigit(b);
            if (digit < 0) continue;            // пробелы и переводы строк игнорируются

            if (high < 0)
            {
                high = digit;
            }
            else
            {
                output.WriteByte((byte)((high << 4) | digit));
                high = -1;
            }
        }

        // Нечётное число цифр: последняя дополняется нулём — так требует спецификация.
        if (high >= 0)
            output.WriteByte((byte)(high << 4));

        return output.ToArray();
    }

    private static int HexDigit(byte b) => b switch
    {
        >= (byte)'0' and <= (byte)'9' => b - '0',
        >= (byte)'a' and <= (byte)'f' => b - 'a' + 10,
        >= (byte)'A' and <= (byte)'F' => b - 'A' + 10,
        _ => -1,
    };

    private static byte[]? Ascii85(byte[] data)
    {
        var output = new MemoryStream(data.Length * 4 / 5 + 4);
        // Буфер под сокращение «z» держим вне цикла: stackalloc в цикле переполняет стек.
        byte[] zeros = new byte[4];
        uint group = 0;
        int count = 0;
        int start = 0;

        // Необязательный вводный маркер «<~».
        if (data.Length >= 2 && data[0] == (byte)'<' && data[1] == (byte)'~')
            start = 2;

        for (int i = start; i < data.Length; i++)
        {
            byte b = data[i];

            if (b == (byte)'~') break;                       // «~>» — конец данных
            if (b is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t' or 0 or 12) continue;

            if (b == (byte)'z' && count == 0)
            {
                output.Write(zeros, 0, 4);                   // «z» = четыре нулевых байта
                continue;
            }

            if (b < (byte)'!' || b > (byte)'u')
                return null;                                 // мусор в данных — лучше не трогать растр

            group = group * 85 + (uint)(b - '!');
            if (++count != 5) continue;

            WriteGroup(output, group, 4);
            group = 0;
            count = 0;
        }

        if (count > 0)
        {
            // Неполная группа дополняется символом «u» до пяти.
            for (int i = count; i < 5; i++)
                group = group * 85 + ('u' - '!');
            WriteGroup(output, group, count - 1);
        }

        return output.ToArray();

        static void WriteGroup(MemoryStream output, uint value, int bytes)
        {
            Span<byte> buffer = stackalloc byte[4];
            buffer[0] = (byte)(value >> 24);
            buffer[1] = (byte)(value >> 16);
            buffer[2] = (byte)(value >> 8);
            buffer[3] = (byte)value;
            output.Write(buffer[..bytes]);
        }
    }

    private static byte[]? RunLength(byte[] data)
    {
        var output = new MemoryStream(data.Length * 2);
        int i = 0;

        while (i < data.Length)
        {
            int length = data[i++];
            if (length == 128) break;                        // признак конца данных

            if (length < 128)
            {
                int copy = length + 1;
                if (i + copy > data.Length) return null;
                output.Write(data, i, copy);
                i += copy;
            }
            else
            {
                if (i >= data.Length) return null;
                byte value = data[i++];
                for (int n = 0; n < 257 - length; n++)
                    output.WriteByte(value);
            }
        }

        return output.ToArray();
    }
}
