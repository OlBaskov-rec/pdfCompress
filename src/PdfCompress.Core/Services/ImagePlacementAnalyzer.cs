using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;

namespace PdfCompress.Core.Services;

/// <summary>Наибольший размер (в пунктах, 1 пункт = 1/72 дюйма), с которым растр выводится в документе.</summary>
public sealed record ImagePlacement(double WidthPt, double HeightPt);

/// <summary>
/// Определяет, насколько крупно каждый растр реально показан на странице. Без этого «целевое dpi»
/// не имеет смысла: картинка 2000×1500 может занимать всю страницу A4 (≈170 dpi — трогать нечего)
/// или клетку таблицы в один сантиметр (≈5000 dpi — можно ужать в 30 раз).
///
/// Для этого разбираются потоки содержимого страниц: отслеживается матрица преобразования (CTM)
/// по операторам <c>q</c>/<c>Q</c>/<c>cm</c>, и на каждом <c>Do</c> вычисляется размер единичного
/// квадрата изображения в пользовательских координатах. Form XObject'ы разбираются рекурсивно.
/// </summary>
public static class ImagePlacementAnalyzer
{
    /// <summary>Ограничение глубины вложенности форм — страховка от циклических ссылок в битых PDF.</summary>
    private const int MaxFormDepth = 8;

    /// <summary>Матрица PDF [a b c d e f]: сдвиг+поворот+масштаб.</summary>
    private readonly record struct Matrix(double A, double B, double C, double D, double E, double F)
    {
        public static readonly Matrix Identity = new(1, 0, 0, 1, 0, 0);

        /// <summary>Умножение this × other (порядок PDF: новая матрица применяется первой).</summary>
        public Matrix Multiply(in Matrix o) => new(
            A * o.A + B * o.C,
            A * o.B + B * o.D,
            C * o.A + D * o.C,
            C * o.B + D * o.D,
            E * o.A + F * o.C + o.E,
            E * o.B + F * o.D + o.F);

        /// <summary>Длина образа горизонтального единичного вектора — ширина картинки на странице.</summary>
        public double ScaleX => Math.Sqrt(A * A + B * B);

        /// <summary>Длина образа вертикального единичного вектора — высота картинки на странице.</summary>
        public double ScaleY => Math.Sqrt(C * C + D * D);
    }

    /// <summary>
    /// Обходит все страницы и возвращает для каждого растрового XObject максимальный размер вывода.
    /// Битые страницы пропускаются: анализ — оптимизация, а не обязательный этап.
    /// </summary>
    public static Dictionary<PdfObjectID, ImagePlacement> Analyze(PdfDocument document)
    {
        var map = new Dictionary<PdfObjectID, ImagePlacement>();

        foreach (var page in document.Pages)
        {
            try
            {
                var content = ContentReader.ReadContent(page);
                var resources = page.Elements.GetDictionary("/Resources");
                Walk(content, resources, Matrix.Identity, map, 0);
            }
            catch (Exception)
            {
                // Нестандартный или повреждённый поток содержимого — просто нет данных по этой
                // странице; изображения из неё будут ужаты по запасному правилу.
            }
        }

        return map;
    }

    private static void Walk(
        CSequence content,
        PdfDictionary? resources,
        Matrix ctm,
        Dictionary<PdfObjectID, ImagePlacement> map,
        int depth)
    {
        var stack = new Stack<Matrix>();

        foreach (var item in content)
        {
            if (item is not COperator op)
                continue;

            switch (op.OpCode.OpCodeName)
            {
                case OpCodeName.q:
                    stack.Push(ctm);
                    break;

                case OpCodeName.Q:
                    if (stack.Count > 0) ctm = stack.Pop();
                    break;

                case OpCodeName.cm:
                    if (TryReadMatrix(op, out var m))
                        ctm = m.Multiply(ctm);
                    break;

                case OpCodeName.Do:
                    if (op.Operands.Count > 0 && op.Operands[^1] is CName name)
                        Resolve(name.Name, resources, ctm, map, depth);
                    break;
            }
        }
    }

    private static void Resolve(
        string name,
        PdfDictionary? resources,
        Matrix ctm,
        Dictionary<PdfObjectID, ImagePlacement> map,
        int depth)
    {
        var xobjects = resources?.Elements.GetDictionary("/XObject");
        var xobject = xobjects?.Elements.GetDictionary(name);
        if (xobject is null)
            return;

        string subtype = xobject.Elements.GetName("/Subtype");

        if (subtype == "/Image")
        {
            Record(map, PdfObjectIds.Of(xobject), ctm.ScaleX, ctm.ScaleY);
            return;
        }

        if (subtype != "/Form" || depth >= MaxFormDepth || xobject.Stream is null)
            return;

        // У формы своя матрица и (обычно) свои ресурсы; если ресурсов нет — наследуются родительские.
        var formCtm = TryReadMatrixArray(xobject.Elements.GetArray("/Matrix"), out var fm)
            ? fm.Multiply(ctm)
            : ctm;

        try
        {
            var inner = ContentReader.ReadContent(xobject.Stream.UnfilteredValue);
            Walk(inner, xobject.Elements.GetDictionary("/Resources") ?? resources, formCtm, map, depth + 1);
        }
        catch (Exception)
        {
            // Форму разобрать не удалось — её изображения останутся без данных о размере.
        }
    }

    /// <summary>Запоминает НАИБОЛЬШИЙ из размеров: картинку могли вставить и мелко, и крупно.</summary>
    private static void Record(Dictionary<PdfObjectID, ImagePlacement> map, PdfObjectID id, double w, double h)
    {
        if (id.IsEmpty || w <= 0 || h <= 0 || double.IsNaN(w) || double.IsNaN(h))
            return;

        map[id] = map.TryGetValue(id, out var existing)
            ? new ImagePlacement(Math.Max(existing.WidthPt, w), Math.Max(existing.HeightPt, h))
            : new ImagePlacement(w, h);
    }

    private static bool TryReadMatrix(COperator op, out Matrix matrix)
    {
        matrix = Matrix.Identity;
        if (op.Operands.Count < 6)
            return false;

        Span<double> v = stackalloc double[6];
        // Берём ПОСЛЕДНИЕ шесть операндов: в потоке перед оператором могут остаться лишние.
        int first = op.Operands.Count - 6;
        for (int i = 0; i < 6; i++)
        {
            if (op.Operands[first + i] is not CNumber n)
                return false;
            v[i] = n is CInteger ci ? ci.Value : ((CReal)n).Value;
        }

        matrix = new Matrix(v[0], v[1], v[2], v[3], v[4], v[5]);
        return true;
    }

    private static bool TryReadMatrixArray(PdfArray? array, out Matrix matrix)
    {
        matrix = Matrix.Identity;
        if (array is null || array.Elements.Count < 6)
            return false;

        Span<double> v = stackalloc double[6];
        for (int i = 0; i < 6; i++)
            v[i] = array.Elements.GetReal(i);

        matrix = new Matrix(v[0], v[1], v[2], v[3], v[4], v[5]);
        return true;
    }
}
