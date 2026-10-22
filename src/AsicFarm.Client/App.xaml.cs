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
        // 1. Глобальные обработчики для отладки падений
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

        // 2. КРИТИЧЕСКИ ВАЖНО: CurrentUICulture = en-US, чтобы WPF UI находил свои ресурсы.
        // CurrentCulture = ru-RU, чтобы у тебя в коде форматирование чисел/дат было русским (если надо).
        var displayCulture = CultureInfo.GetCultureInfo("ru-RU");
        var uiCulture = CultureInfo.GetCultureInfo("en-US");

        CultureInfo.DefaultThreadCurrentCulture = displayCulture;
        CultureInfo.DefaultThreadCurrentUICulture = uiCulture;
        Thread.CurrentThread.CurrentCulture = displayCulture;
        Thread.CurrentThread.CurrentUICulture = uiCulture;

        // 3. Переопределяем Language для WPF (это то, что читает XmlLanguage).
        // Используем en-US — он всегда есть в любой системе и не вызывает ошибок fallback.
        var wpfLanguage = XmlLanguage.GetLanguage("en-US");

        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(wpfLanguage));

        // 4. Дополнительно для <Run> и других TextElement'ов (лечит ошибку в биндингах внутри TextBlock)
        FrameworkContentElement.LanguageProperty.OverrideMetadata(
            typeof(System.Windows.Documents.TextElement),
            new FrameworkPropertyMetadata(wpfLanguage));

        // 5. Инициализация DI
        try
        {
            var services = new ServiceCollection();
            services.AddLogging(b => b.AddDebug().SetMinimumLevel(LogLevel.Information));
            services.AddSingleton<HttpClient>(_ => new HttpClient { Timeout = TimeSpan.FromMinutes(3) });
            // В метод OnStartup, в блок инициализации сервисов, добавь:
            services.AddTransient<MinerSettingsViewModel>();

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