using System.Net;
using System.Net.Http;
using Avalonia;
using PdfCompress.Desktop.Services;
using Velopack;

namespace PdfCompress.Desktop;

class Program
{
    // Ничего из Avalonia и сторонних библиотек нельзя трогать до AppMain: до этого момента
    // среда ещё не инициализирована.
    [STAThread]
    public static void Main(string[] args)
    {
        // Диагностика: любые необработанные исключения — в %AppData%/PdfCompress/log.txt.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppLog.Error("Необработанное исключение", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Error("Непронаблюдённое исключение задачи", e.Exception);
            e.SetObserved();
        };

        // В ряде корпоративных сетей системный прокси перехватывает TLS (подменяет сертификат),
        // из-за чего проверка обновлений падает с SSL-ошибкой. Приложение ходит в сеть только за
        // обновлениями GitHub, где прямое соединение работает и проверяется НАСТОЯЩИЙ сертификат,
        // поэтому обходим прокси для всего процесса (пустой WebProxy = без прокси).
        HttpClient.DefaultProxy = new WebProxy();

        // Должно идти ПЕРВЫМ: Velopack обрабатывает хуки установки и обновления
        // (--veloapp-install и т. п.) и при необходимости завершает процесс до старта интерфейса.
        VelopackApp.Build().Run();

        AppLog.Info($"Запуск v{AppInfo.Version}");
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            AppLog.Error("Фатальная ошибка приложения", ex);
            throw;
        }
    }

    // Конфигурация Avalonia; используется также визуальным конструктором — не удалять.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
