using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfCompress.Desktop.Services;

namespace PdfCompress.Desktop.ViewModels;

/// <summary>
/// Авто-обновление через Velopack: фоновая проверка при старте, ручная из окна «О программе»,
/// скачивание и установка.
///
/// Главное правило здесь — обновление никогда не мешает основной работе: вся сетевая возня идёт
/// на пуле потоков и под таймаутом, а любая ошибка уходит в журнал, а не в лицо пользователю.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Версия приложения — для окна «О программе».</summary>
    public string AppVersionText => $"Версия {AppInfo.Version}";

    /// <summary>Обновление найдено: значок «ℹ» подсвечивается зелёным.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCheckButton))]
    [NotifyPropertyChangedFor(nameof(ShowRevealButton))]
    [NotifyPropertyChangedFor(nameof(UpdateFoundButtonText))]
    private bool isUpdateAvailable;

    /// <summary>Обновление скачано и готово к установке: внизу показан баннер.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCheckButton))]
    [NotifyPropertyChangedFor(nameof(ShowRevealButton))]
    private bool isUpdateReady;

    /// <summary>Текст баннера готового обновления.</summary>
    [ObservableProperty]
    private string updateText = string.Empty;

    /// <summary>Строка состояния внутри окна «О программе».</summary>
    [ObservableProperty]
    private string updateCheckStatus = string.Empty;

    /// <summary>
    /// Сколько секунд ждать ответа на одну попытку проверки. Замеры на рабочей сети:
    /// api.github.com отвечает то за 0,6 с, то за 17 с, то не отвечает вовсе — поэтому
    /// 30 секунд мало, а бесконечно ждать нельзя.
    /// </summary>
    private const int CheckTimeoutSeconds = 45;

    /// <summary>Попыток при ручной проверке — пользователь ждёт ответа и видит счётчик.</summary>
    private const int ManualCheckAttempts = 3;

    /// <summary>Попыток при фоновой проверке: тише и короче, результат никто не ждёт.</summary>
    private const int BackgroundCheckAttempts = 2;

    /// <summary>
    /// Сколько ждать загрузку пакета. Обновление — это полсотни мегабайт (дельта меньше, но
    /// с неизвестной Velopack версии приходит полный пакет), и на небыстром канале это минуты.
    /// Прежние две минуты приводили к тому, что загрузка успевала дойти до конца уже ПОСЛЕ
    /// того, как приложение сдалось: баннер не появлялся, хотя пакет лежал на диске.
    /// </summary>
    private const int DownloadTimeoutSeconds = 600;

    private bool _updateDownloaded;

    /// <summary>
    /// Уже идущая загрузка. Таймаут только перестаёт ждать, но работу не отменяет, поэтому
    /// повторное нажатие должно дождаться ТУ ЖЕ загрузку, а не начать качать те же мегабайты
    /// заново.
    /// </summary>
    private Task? _downloadTask;

    private string _availableVersion = string.Empty;

    /// <summary>
    /// Защёлка от параллельных проверок (фоновая при старте плюс ручная): UpdateService держит
    /// найденное обновление в одном поле и на конкурентный доступ не рассчитан.
    /// </summary>
    private int _updateBusy;

    /// <summary>Кнопка ручной проверки — пока обновление не найдено.</summary>
    public bool ShowCheckButton => !IsUpdateAvailable;

    /// <summary>Зелёная кнопка «Обнаружено обновление» — найдено, но баннер ещё не показан.</summary>
    public bool ShowRevealButton => IsUpdateAvailable && !IsUpdateReady;

    public string UpdateFoundButtonText => $"Обнаружено обновление {_availableVersion}";

    [RelayCommand]
    private void ApplyUpdate() => _updateService.ApplyAndRestart();

    /// <summary>«Отложить»: убрать баннер. Значок остаётся зелёным — обновиться можно позже.</summary>
    [RelayCommand]
    private void PostponeUpdate() => IsUpdateReady = false;

    /// <summary>Ручная проверка из окна «О программе».</summary>
    [RelayCommand]
    private async Task CheckUpdatesManualAsync()
    {
        if (!_updateService.IsSupported)
        {
            UpdateCheckStatus = "Проверка обновлений работает только в установленной сборке " +
                                "(portable или Setup), но не при запуске из исходников.";
            return;
        }

        if (Interlocked.Exchange(ref _updateBusy, 1) == 1)
        {
            UpdateCheckStatus = "Проверка уже идёт…";
            return;
        }

        UpdateCheckStatus = "Проверка…";
        try
        {
            // Таймаут обязателен: без него при недоступной сети статус навсегда застрял бы
            // на «Проверка…», а кнопка осталась бы заблокированной. А повторы обязательны
            // потому, что api.github.com (куда ходит Velopack) отвечает неровно: замеры на
            // рабочей сети дали 0,6 с, 17 с и полный таймаут вперемешку, тогда как сам
            // github.com стабильно отвечает за секунду. С одной попытки проверка просто
            // не доходит примерно в трети случаев.
            string? version = null;
            for (int attempt = 1; attempt <= ManualCheckAttempts; attempt++)
            {
                try
                {
                    version = await RunWithTimeout(() => _updateService.CheckAsync(), CheckTimeoutSeconds);
                    break;
                }
                catch (TimeoutException) when (attempt < ManualCheckAttempts)
                {
                    UpdateCheckStatus = $"Сеть не ответила, повтор ({attempt + 1} из {ManualCheckAttempts})…";
                }
            }

            if (version is null)
            {
                UpdateCheckStatus = "Установлена последняя версия.";
                return;
            }

            _availableVersion = version;
            IsUpdateAvailable = true;
            UpdateCheckStatus = $"Найдено обновление {version}, скачиваю…";

            if (!await TryDownloadAsync())
            {
                UpdateCheckStatus = $"Обновление {version} ещё скачивается. Загрузка продолжается — " +
                                    "загляните сюда позже.";
                return;
            }

            // Ручная проверка — явное действие пользователя, поэтому баннер показываем сразу.
            UpdateText = $"Готово обновление {version}. Установить и перезапустить программу?";
            UpdateCheckStatus = $"Обновление {version} скачано.";
            IsUpdateReady = true;
        }
        catch (TimeoutException)
        {
            UpdateCheckStatus = IsUpdateReady
                ? $"Обновление {_availableVersion} скачано."
                : "Сеть не ответила вовремя. Попробуйте позже.";
        }
        catch (Exception ex)
        {
            AppLog.Error("Ручная проверка обновлений не удалась", ex);
            UpdateCheckStatus = IsUpdateReady
                ? $"Обновление {_availableVersion} скачано."
                : $"Не удалось проверить: {DescribeError(ex)}";
        }
        finally
        {
            Interlocked.Exchange(ref _updateBusy, 0);
        }
    }

    /// <summary>«Обнаружено обновление»: докачивает при необходимости и показывает баннер.</summary>
    [RelayCommand]
    private async Task RevealUpdateAsync()
    {
        if (!IsUpdateAvailable) return;

        if (!_updateDownloaded)
        {
            UpdateCheckStatus = $"Скачиваю обновление {_availableVersion}…";
            try
            {
                if (!await TryDownloadAsync())
                {
                    UpdateCheckStatus = $"Обновление {_availableVersion} ещё скачивается. " +
                                        "Загрузка продолжается — нажмите ещё раз чуть позже.";
                    return;
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Скачивание обновления не удалось", ex);
                UpdateCheckStatus = $"Не удалось скачать: {DescribeError(ex)}";
                return;
            }
        }

        UpdateText = $"Готово обновление {_availableVersion}. Установить и перезапустить программу?";
        UpdateCheckStatus = $"Обновление {_availableVersion} скачано.";
        IsUpdateReady = true;
    }

    /// <summary>
    /// Фоновая проверка при старте. В режиме разработки — безопасный no-op.
    /// Находит и заранее скачивает обновление, но баннер НЕ показывает: только подсвечивает
    /// значок «ℹ». Прерывать работу пользователя из-за обновления неправильно.
    /// </summary>
    public async Task CheckForUpdatesAsync()
    {
        if (Interlocked.Exchange(ref _updateBusy, 1) == 1)
            return;

        try
        {
            // Вся работа Velopack (в том числе синхронные участки и сеть) — на пуле потоков и
            // под таймаутом: запуск приложения не должен подвисать из-за проверки. Повтор —
            // потому что api.github.com отвечает неровно (см. CheckTimeoutSeconds).
            string? version = null;
            for (int attempt = 1; attempt <= BackgroundCheckAttempts; attempt++)
            {
                try
                {
                    version = await RunWithTimeout(() => _updateService.CheckAsync(), CheckTimeoutSeconds);
                    break;
                }
                catch (TimeoutException)
                {
                    if (attempt == BackgroundCheckAttempts)
                        return; // сеть не ответила — тихо выходим, программа работает
                }
            }

            if (version is null)
                return;

            _availableVersion = version;
            IsUpdateAvailable = true;

            // Качаем заранее, чтобы по нажатию «Обнаружено обновление» применилось мгновенно.
            try
            {
                await Task.Run(() => _updateService.DownloadAsync());
                _updateDownloaded = true;
            }
            catch (Exception ex)
            {
                AppLog.Error("Фоновое скачивание обновления не удалось (докачаем по требованию)", ex);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Фоновая проверка обновлений не удалась", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _updateBusy, 0);
        }
    }

    /// <summary>
    /// Дожидается загрузки пакета, переиспользуя уже идущую. Возвращает false, если за
    /// отведённое время загрузка не закончилась — но она продолжает идти, и следующий вызов
    /// подхватит её же.
    /// </summary>
    private async Task<bool> TryDownloadAsync()
    {
        if (_updateDownloaded)
            return true;

        _downloadTask ??= Task.Run(() => _updateService.DownloadAsync());

        var finished = await Task.WhenAny(_downloadTask, Task.Delay(TimeSpan.FromSeconds(DownloadTimeoutSeconds)));
        if (finished != _downloadTask)
            return false;

        try
        {
            await _downloadTask; // пробрасываем исключение загрузки, если оно было
        }
        catch
        {
            _downloadTask = null; // сбой — следующая попытка начнёт заново
            throw;
        }

        _updateDownloaded = true;
        return true;
    }

    /// <summary>Разворачивает цепочку вложенных исключений в одну строку — для диагностики.</summary>
    private static string DescribeError(Exception ex)
    {
        var text = new StringBuilder();
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (text.Length > 0) text.Append(" → ");
            text.Append(e.Message);
        }
        return text.ToString();
    }

    /// <summary>Выполняет работу на пуле потоков с таймаутом.</summary>
    private static async Task<T> RunWithTimeout<T>(Func<Task<T>> work, int seconds)
    {
        var task = Task.Run(work);
        if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds))) != task)
            throw new TimeoutException();
        return await task;
    }

}
