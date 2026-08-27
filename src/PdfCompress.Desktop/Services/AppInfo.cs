using System.Reflection;

namespace PdfCompress.Desktop.Services;

/// <summary>Сведения о приложении: версия — единая точка для заголовка окна и окна «О программе».</summary>
public static class AppInfo
{
    /// <summary>Версия приложения из сборки (источник — &lt;Version&gt; в csproj).</summary>
    public static string Version { get; } = ComputeVersion();

    private static string ComputeVersion()
    {
        var asm = typeof(AppInfo).Assembly;
        // InformationalVersion может содержать суффикс сборки (+hash) — отбрасываем его.
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(info))
            return info.Split('+')[0];

        var v = asm.GetName().Version;
        return v is null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
