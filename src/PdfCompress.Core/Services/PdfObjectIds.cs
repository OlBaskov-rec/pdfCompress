using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace PdfCompress.Core.Services;

/// <summary>
/// Единая точка получения номера объекта PDF. Само свойство <c>PdfObject.ObjectID</c> из
/// библиотеки наружу не открыто, поэтому пользуемся публичным «служебным» API PdfSharp —
/// и делаем это в одном месте, чтобы смена версии библиотеки правилась одной строкой.
/// </summary>
internal static class PdfObjectIds
{
    public static PdfObjectID Of(PdfObject obj) => PdfInternals.GetObjectID(obj);
}
