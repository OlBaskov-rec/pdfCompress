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

    private bool _updateDownloaded;
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
            // на «Проверка…», а кнопка осталась бы заблокированной.
            var version = await RunWithTimeout(() => _updateService.CheckAsync(), 30);
            if (version is null)
            {
                UpdateCheckStatus = "Установлена последняя версия.";
                return;
            }

            _availableVersion = version;
            IsUpdateAvailable = true;
            UpdateCheckStatus = $"Найдено обновление {version}, скачиваю…";

            await RunWithTimeout(() => _updateService.DownloadAsync(), 120);
            _updateDownloaded = true;

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
                await RunWithTimeout(() => _updateService.DownloadAsync(), 120);
                _updateDownloaded = true;
            }
            catch (TimeoutException)
            {
                UpdateCheckStatus = "Сеть не ответила вовремя. Попробуйте позже.";
                return;
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
            // под таймаутом: запуск приложения не должен подвисать из-за проверки.
            var check = Task.Run(() => _updateService.CheckAsync());
            if (await Task.WhenAny(check, Task.Delay(TimeSpan.FromSeconds(20))) != check)
                return; // сеть не ответила — тихо выходим, программа работает

            var version = await check;
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

    /// <summary>Перегрузка для операций без результата.</summary>
    private static async Task RunWithTimeout(Func<Task> work, int seconds)
    {
        var task = Task.Run(work);
        if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds))) != task)
            throw new TimeoutException();
        await task;
    }
}
