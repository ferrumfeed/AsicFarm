using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Markup;
using AsicFarm.Client.Api;
using AsicFarm.Client.Services;
using AsicFarm.Client.ViewModels;
using AutoUpdaterDotNET;
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
        try { ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Theme apply error: {ex.Message}"); }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show($"UI Exception:\n{args.Exception.Message}\n\nStack:\n{args.Exception.StackTrace}", "Критическая ошибка UI", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
            Shutdown();
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                MessageBox.Show($"Fatal Exception:\n{ex.Message}\n\nStack:\n{ex.StackTrace}", "Критическая ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        var culture = CultureInfo.GetCultureInfo("ru-RU");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        var lang = XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(lang));
        FrameworkContentElement.LanguageProperty.OverrideMetadata(typeof(System.Windows.Documents.TextElement), new FrameworkPropertyMetadata(lang));

        try
        {
            var services = new ServiceCollection();
            services.AddLogging(b => b.AddDebug().SetMinimumLevel(LogLevel.Information));
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

            // --- УСКОРЕННОЕ АВТООБНОВЛЕНИЕ ---
            // Ждем 1.5 секунды, чтобы окно прогрузилось, и запускаем проверку в фоне
            Task.Delay(1500).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() => SetupAutoUpdater());
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при запуске:\n{ex.Message}\n\nStack:\n{ex.StackTrace}", "Ошибка запуска", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }

        base.OnStartup(e);
    }

    private void SetupAutoUpdater()
    {
        AutoUpdater.ReportErrors = false; // Не спамим ошибками, если сервер временно недоступен
        AutoUpdater.ParseUpdateInfoEvent += AutoUpdaterOnParseUpdateInfoEvent;
        AutoUpdater.ApplicationExitEvent += () => { Shutdown(); };

        AutoUpdater.Start("http://89.125.129.150:5000/api/v1/updates/latest");
    }

    private void AutoUpdaterOnParseUpdateInfoEvent(ParseUpdateInfoEventArgs args)
    {
        try
        {
            using var doc = JsonDocument.Parse(args.RemoteData);
            var root = doc.RootElement;

            var updateInfo = new UpdateInfoEventArgs
            {
                CurrentVersion = root.GetProperty("version").GetString(),
                ChangelogURL = root.TryGetProperty("changelog", out var ch) ? ch.GetString() : "",
                DownloadURL = root.TryGetProperty("url", out var url) ? url.GetString() : "",
            };

            if (root.TryGetProperty("mandatory", out var mandatory))
            {
                var mode = mandatory.TryGetProperty("mode", out var m) ? m.GetInt32() : 0;
                updateInfo.Mandatory = new Mandatory
                {
                    Value = mandatory.TryGetProperty("value", out var v) && v.GetBoolean(),
                    UpdateMode = (Mode)mode,
                    MinimumVersion = mandatory.TryGetProperty("minVersion", out var mv) ? mv.GetString() : "0.0.0.0"
                };
            }

            if (root.TryGetProperty("checksum", out var checksum))
            {
                updateInfo.CheckSum = new CheckSum
                {
                    Value = checksum.GetProperty("value").GetString(),
                    HashingAlgorithm = checksum.GetProperty("hashingAlgorithm").GetString()
                };
            }

            args.UpdateInfo = updateInfo;
        }
        catch { /* Игнорируем ошибки парсинга, чтобы не спамить */ }
    }
}