using System.Net.Http;
using System.Windows;
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
        try { ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light); } catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddDebug().SetMinimumLevel(LogLevel.Information));
        services.AddSingleton<HttpClient>(_ => new HttpClient { Timeout = TimeSpan.FromMinutes(3) });

        services.AddSingleton<ServerApiClient>();
        services.AddSingleton<TelemetryHubClient>();
        services.AddSingleton<BitcoinNetworkService>();
        services.AddSingleton<PoolsViewModel>(); // НОВАЯ СТРОКА

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

        try
        {
            var window = Services.GetRequiredService<MainWindow>();
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Критическая ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }

        base.OnStartup(e);
    }
}