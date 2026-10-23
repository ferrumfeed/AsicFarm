using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using AsicFarm.Client.Api;
using AsicFarm.Client.Services;
using AsicFarm.Client.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Appearance;

namespace AsicFarm.Client;

public partial class App : Application
{
    public static ServiceProvider Services { get; private set; } = null!;
    public static bool IsDark { get; private set; } = true;

    public static void ApplyTheme(bool dark)
    {
        IsDark = dark;
        try
        {
            ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Theme apply error: {ex.Message}");
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Глобальные обработчики для отладки падений
        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show(
                $"UI Exception:\n{args.Exception.Message}\n\nStack:\n{args.Exception.StackTrace}",
                "Критическая ошибка UI", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
            Shutdown();
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                MessageBox.Show(
                    $"Fatal Exception:\n{ex.Message}\n\nStack:\n{ex.StackTrace}",
                    "Критическая ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            MessageBox.Show(
                $"Task Exception:\n{args.Exception.InnerException?.Message}",
                "Ошибка в фоне", MessageBoxButton.OK, MessageBoxImage.Error);
            args.SetObserved();
        };

        // Устанавливаем культуру потока
        var culture = CultureInfo.GetCultureInfo("ru-RU");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        // Переопределяем Language для WPF
        var lang = XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);

        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(lang));

        FrameworkContentElement.LanguageProperty.OverrideMetadata(
            typeof(System.Windows.Documents.TextElement),
            new FrameworkPropertyMetadata(lang));

        // Инициализация DI
        try
        {
            var services = new ServiceCollection();
            services.AddLogging(b => b.AddDebug().SetMinimumLevel(LogLevel.Information));

            // УВЕЛИЧЕН ТАЙМАУТ ДО 5 МИНУТ
            services.AddSingleton<HttpClient>(_ => new HttpClient { Timeout = TimeSpan.FromMinutes(5) });

            services.AddSingleton<ServerApiClient>();
            services.AddSingleton<TelemetryHubClient>();
            services.AddSingleton<BitcoinNetworkService>();
            services.AddSingleton<PoolsViewModel>();

            services.AddSingleton<ScheduleViewModel>(sp =>
            {
                var api = sp.GetRequiredService<ServerApiClient>();
                var log = sp.GetRequiredService<ILogger<ScheduleViewModel>>();
                return new ScheduleViewModel(api, () => new List<MinerItemViewModel>(), log);
            });

            services.AddSingleton<AlertsViewModel>();
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();

            Services = services.BuildServiceProvider();

            var window = Services.GetRequiredService<MainWindow>();
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Ошибка при запуске:\n{ex.Message}\n\nInnerException:\n{ex.InnerException?.Message}\n\nStack:\n{ex.StackTrace}",
                "Ошибка запуска", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }

        base.OnStartup(e);
    }
}