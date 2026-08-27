using PdfCompress.Core.Models;

namespace PdfCompress.Desktop.ViewModels;

/// <summary>Пункт выпадающего списка единиц измерения размера.</summary>
public sealed record SizeUnitOption(SizeUnit Unit, string Title)
{
    /// <summary>Порядок как в списке: от крупных к мелким читается хуже, поэтому идём по возрастанию.</summary>
    public static IReadOnlyList<SizeUnitOption> All { get; } = new[]
    {
        new SizeUnitOption(SizeUnit.Bytes, "байты"),
        new SizeUnitOption(SizeUnit.Kilobytes, "КБ"),
        new SizeUnitOption(SizeUnit.Megabytes, "МБ"),
        new SizeUnitOption(SizeUnit.Gigabytes, "ГБ"),
    };

    public override string ToString() => Title;
}
