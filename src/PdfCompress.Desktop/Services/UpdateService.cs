using Velopack;
using Velopack.Sources;

namespace PdfCompress.Desktop.Services;

/// <summary>
/// Авто-обновление через Velopack. Источник — релизы GitHub (публичный репозиторий).
/// В режиме разработки (приложение не установлено через Velopack) все операции — безопасный no-op.
/// </summary>
public class UpdateService
{
    private const string RepoUrl = "https://github.com/OlBaskov-rec/pdfCompress";

    private readonly UpdateManager _manager;
    private UpdateInfo? _pending;

    public UpdateService()
    {
        // accessToken = null — репозиторий публичный; prerelease = false.
        _manager = new UpdateManager(new GithubSource(RepoUrl, null, false));
    }

    /// <summary>true только для установленной Velopack-сборки (не в dev-запуске).</summary>
    public bool IsSupported => _manager.IsInstalled;

    /// <summary>Проверяет наличие обновления. Возвращает версию или null.</summary>
    public async Task<string?> CheckAsync()
    {
        if (!_manager.IsInstalled)
            return null;

        _pending = await _manager.CheckForUpdatesAsync();
        return _pending?.TargetFullRelease.Version.ToString();
    }

    /// <summary>Скачивает найденное обновление в фоне.</summary>
    public async Task DownloadAsync()
    {
        if (_pending != null)
            await _manager.DownloadUpdatesAsync(_pending);
    }

    /// <summary>Применяет обновление и перезапускает приложение.</summary>
    public void ApplyAndRestart()
    {
        if (_pending != null)
            _manager.ApplyUpdatesAndRestart(_pending);
    }
}
