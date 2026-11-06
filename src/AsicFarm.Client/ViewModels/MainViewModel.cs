using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using AsicFarm.Client.Api;
using AsicFarm.Client.Services;
using AsicFarm.Client.Views;
using AsicFarm.Shared.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AsicFarm.Client.ViewModels;

public sealed class WatchdogFilterItem
{
    public string Id { get; }
    public string Name { get; }
    public DateTimeOffset LastSeen { get; }
    public int MinerCount { get; }

    public WatchdogFilterItem(string id, string name, DateTimeOffset lastSeen, int minerCount)
    {
        Id = id; Name = name; LastSeen = lastSeen; MinerCount = minerCount;
    }
    public string DisplayName => Id == "" ? $"Все агенты ({MinerCount})" : $"{Id} ({MinerCount})";
}

public sealed partial class PoolPingItem : ObservableObject
{
    public string Name { get; }
    public string Host { get; }
    public int Port { get; }

    [ObservableProperty] private long _pingMs = -2; // -2 = "измеряется", -1 = "ошибка", >=0 = пинг
    [ObservableProperty] private string _statusColor = "#6b7280"; // серый по умолчанию

    public string PingDisplay => PingMs switch
    {
        -2 => "Проверка...",
        -1 => "Нет связи",
        _ => $"{PingMs} мс"
    };

    public PoolPingItem(string name, string host, int port)
    {
        Name = name; Host = host; Port = port;
    }

    partial void OnPingMsChanged(long value)
    {
        OnPropertyChanged(nameof(PingDisplay));
        if (value == -2) StatusColor = "#6b7280";
        else if (value == -1) StatusColor = "#ef4444"; // красный
        else if (value < 50) StatusColor = "#5cb85c";  // зеленый
        else if (value < 150) StatusColor = "#f0ad4e"; // оранжевый
        else StatusColor = "#ef4444";                  // красный
    }
}

public sealed partial class MainViewModel : ObservableObject
{
    private const decimal BlockRewardBtc = 3.125m;
    private const double SecondsPerDay = 86400.0;
    private const double TwoPow32 = 4294967296.0;

    private readonly ServerApiClient _api;
    private readonly TelemetryHubClient _hub;
    private readonly BitcoinNetworkService _btc;
    private readonly HttpClient _httpClient;
    private readonly ILogger<MainViewModel> _log;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<string, MinerItemViewModel> _index = new();
    private readonly ClientConfig _config;
    private readonly System.Timers.Timer _refreshTimer;

    [ObservableProperty] private string _serverUrl = "http://localhost:5000";
    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _statusMessage = "Готов к работе";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private int _selectedTab;
    [ObservableProperty] private bool _isDarkTheme = true;
    [ObservableProperty] private double _totalHashrateGhs;
    [ObservableProperty] private int _totalPowerW;
    [ObservableProperty] private int _minerCount;
    [ObservableProperty] private int _onlineMinerCount;
    [ObservableProperty] private decimal _btcPriceUsd;
    [ObservableProperty] private double _networkDifficulty;
    [ObservableProperty] private decimal _dailyBtcEstimate;
    [ObservableProperty] private decimal _dailyUsdEstimate;
    [ObservableProperty] private double _dailyPowerKwh;
    [ObservableProperty] private MinerViewMode _viewMode = MinerViewMode.Cards;
    [ObservableProperty] private MinerSortMode _sortMode = MinerSortMode.ProblemsFirst;
    [ObservableProperty] private bool _showOnlyProblems = false;

    // ПИНГИ
    [ObservableProperty] private long _serverPingMs = -2;
    public ObservableCollection<PoolPingItem> PoolPings { get; } = new();

    public ObservableCollection<MinerItemViewModel> Miners { get; } = new();
    public ICollectionView MinersView { get; private set; } = null!;
    public ObservableCollection<WatchdogFilterItem> WatchdogFilters { get; } = new();
    [ObservableProperty] private WatchdogFilterItem? _selectedWatchdogFilter;

    public ScheduleViewModel Schedules { get; }
    public AlertsViewModel Alerts { get; }
    public SettingsViewModel Settings { get; }
    public PoolsViewModel Pools { get; }

    public IReadOnlyList<MinerViewMode> ViewModes { get; } = new[] { MinerViewMode.Cards, MinerViewMode.Compact };
    public IReadOnlyList<MinerSortMode> SortModes { get; } = new[] { MinerSortMode.ProblemsFirst, MinerSortMode.HashrateAsc, MinerSortMode.HashrateDesc, MinerSortMode.TempDesc, MinerSortMode.EfficiencyDesc, MinerSortMode.NameAsc };

    public string AppVersion
    {
        get
        {
            var v = Assembly.GetEntryAssembly()?.GetName().Version;
            return v == null ? "Версия неизвестна" : $"Версия {v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public string ServerPingDisplay => ServerPingMs switch { -2 => "Подключение...", -1 => "Нет связи", _ => $"{ServerPingMs} мс" };
    public string ServerPingColor => ServerPingMs switch { -2 => "#6b7280", -1 => "#ef4444", < 50 => "#5cb85c", < 150 => "#f0ad4e", _ => "#ef4444" };

    public MainViewModel(
        ServerApiClient api, TelemetryHubClient hub, BitcoinNetworkService btc, HttpClient httpClient,
        ScheduleViewModel schedules, AlertsViewModel alerts, SettingsViewModel settings,
        PoolsViewModel pools, ILoggerFactory loggerFactory, ILogger<MainViewModel> log)
    {
        _api = api; _hub = hub; _btc = btc; _httpClient = httpClient;
        Schedules = schedules; Alerts = alerts; Settings = settings; Pools = pools;
        _loggerFactory = loggerFactory; _log = log; _dispatcher = Dispatcher.CurrentDispatcher;

        Pools.SetMinerProvider(() => Miners.ToList());

        _config = ClientConfig.Load();
        _serverUrl = _config.ServerUrl; _apiKey = _config.ApiKey; _isDarkTheme = _config.IsDarkTheme; _viewMode = _config.ViewMode; _sortMode = _config.SortMode;

        MinersView = CollectionViewSource.GetDefaultView(Miners);
        ApplySorting(); ApplyFilter();

        InitPoolPings();

        _hub.MinerUpdated += OnMinerUpdated;
        _hub.MinerRemoved += OnMinerRemoved;
        _hub.ConnectionChanged += connected =>
        {
            _dispatcher.Invoke(() => { IsConnected = connected; StatusMessage = connected ? $"На связи · {Miners.Count} устройств" : "Попытка переподключения..."; });
        };

        _refreshTimer = new System.Timers.Timer(TimeSpan.FromSeconds(15).TotalMilliseconds) { AutoReset = true };
        _refreshTimer.Elapsed += async (_, _) => await RefreshPingsAsync();
        _refreshTimer.Start();
    }

    private void InitPoolPings()
    {
        PoolPings.Clear();
        PoolPings.Add(new PoolPingItem("BTC (3333)", "btc.k8x.com", 3333));
        PoolPings.Add(new PoolPingItem("BTC (4444)", "btc.k8x.com", 4444));
        PoolPings.Add(new PoolPingItem("BCK (5555)", "bck.k8x.com", 5555));
        PoolPings.Add(new PoolPingItem("LTC+DOGE (8888)", "ltc.k8x.com", 8888));
        PoolPings.Add(new PoolPingItem("LTC+DOGE (6666)", "ltc.k8x.com", 6666));
        PoolPings.Add(new PoolPingItem("BCK (7777)", "bck.k8x.com", 7777));
        PoolPings.Add(new PoolPingItem("ZEC (9999)", "zec.k8x.com", 9999));
        PoolPings.Add(new PoolPingItem("ZEC-1 (9998)", "zc1.k8x.com", 9998));
        PoolPings.Add(new PoolPingItem("ZEC-2 (9997)", "zc2.k8x.com", 9997));
    }

    private async Task RefreshPingsAsync()
    {
        ServerPingMs = -2;
        OnPropertyChanged(nameof(ServerPingDisplay));
        OnPropertyChanged(nameof(ServerPingColor));
        var sPing = await PingService.HttpPingAsync(_httpClient, ServerUrl);
        _dispatcher.Invoke(() =>
        {
            ServerPingMs = sPing;
            OnPropertyChanged(nameof(ServerPingDisplay));
            OnPropertyChanged(nameof(ServerPingColor));
        });

        var tasks = PoolPings.Select(async p =>
        {
            p.PingMs = -2;
            var ping = await PingService.TcpPingAsync(p.Host, p.Port);
            _dispatcher.Invoke(() => p.PingMs = ping);
        });
        await Task.WhenAll(tasks);
    }

    [RelayCommand]
    private void OpenK8xLink()
    {
        try { Process.Start(new ProcessStartInfo { FileName = "https://client.k8x.com/source/6a7o4fvw", UseShellExecute = true }); }
        catch (Exception ex) { _log.LogError(ex, "Failed to open K8X link"); }
    }

    partial void OnSelectedTabChanged(int value) { _ = value switch { 1 => Schedules.LoadAsync(), 2 => Alerts.LoadAsync(), 3 => Settings.LoadAsync(), _ => Task.CompletedTask }; }
    partial void OnIsDarkThemeChanged(bool value) { App.ApplyTheme(value); _config.IsDarkTheme = value; _config.Save(); }
    partial void OnViewModeChanged(MinerViewMode value) { _config.ViewMode = value; _config.Save(); }
    partial void OnSortModeChanged(MinerSortMode value) { _config.SortMode = value; _config.Save(); ApplySorting(); }
    partial void OnShowOnlyProblemsChanged(bool value) { _config.ShowOnlyProblems = value; _config.Save(); ApplyFilter(); }
    partial void OnSelectedWatchdogFilterChanged(WatchdogFilterItem? value) => ApplyFilter();

    private void ApplySorting()
    {
        if (MinersView is null) return;
        MinersView.SortDescriptions.Clear();
        switch (SortMode)
        {
            case MinerSortMode.ProblemsFirst: MinersView.SortDescriptions.Add(new SortDescription(nameof(MinerItemViewModel.ProblemScore), ListSortDirection.Descending)); MinersView.SortDescriptions.Add(new SortDescription(nameof(MinerItemViewModel.HashratePercent), ListSortDirection.Ascending)); break;
            case MinerSortMode.NameAsc: MinersView.SortDescriptions.Add(new SortDescription(nameof(MinerItemViewModel.DisplayName), ListSortDirection.Ascending)); break;
            case MinerSortMode.HashrateDesc: MinersView.SortDescriptions.Add(new SortDescription(nameof(MinerItemViewModel.HashrateGhs), ListSortDirection.Descending)); break;
            case MinerSortMode.HashrateAsc: MinersView.SortDescriptions.Add(new SortDescription(nameof(MinerItemViewModel.HashrateGhs), ListSortDirection.Ascending)); break;
            case MinerSortMode.TempDesc: MinersView.SortDescriptions.Add(new SortDescription(nameof(MinerItemViewModel.ChipMaxTemp), ListSortDirection.Descending)); break;
            case MinerSortMode.EfficiencyDesc: MinersView.SortDescriptions.Add(new SortDescription(nameof(MinerItemViewModel.PowerEfficiencyJPerTh), ListSortDirection.Descending)); break;
        }
        MinersView.Refresh();
    }

    private void ApplyFilter()
    {
        if (MinersView is null) return;
        MinersView.Filter = o =>
        {
            if (o is not MinerItemViewModel m) return false;
            if (ShowOnlyProblems && !m.HasProblem) return false;
            if (SelectedWatchdogFilter is { Id: not "" } wf && m.WatchdogId != wf.Id) return false;
            return true;
        };
        MinersView.Refresh();
    }

    private void RebuildWatchdogFilters()
    {
        var prev = SelectedWatchdogFilter?.Id ?? "";
        WatchdogFilters.Clear();
        WatchdogFilters.Add(new WatchdogFilterItem("", "Все агенты", DateTimeOffset.MinValue, Miners.Count));
        var groups = Miners.GroupBy(m => m.WatchdogId).Select(g => new WatchdogFilterItem(g.Key, g.Key, g.Max(m => m.LastSeen), g.Count())).OrderByDescending(g => g.LastSeen);
        foreach (var g in groups) WatchdogFilters.Add(g);
        SelectedWatchdogFilter = WatchdogFilters.FirstOrDefault(f => f.Id == prev) ?? WatchdogFilters[0];
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerUrl) || string.IsNullOrWhiteSpace(ApiKey)) { StatusMessage = "Введите адрес сервера и ключ доступа"; return; }
        _config.ServerUrl = ServerUrl; _config.ApiKey = ApiKey; _config.IsDarkTheme = IsDarkTheme; _config.ViewMode = ViewMode; _config.SortMode = SortMode; _config.ShowOnlyProblems = ShowOnlyProblems; _config.Save();

        IsLoading = true; StatusMessage = "Установка соединения...";
        try
        {
            _api.Configure(ServerUrl, ApiKey);
            var miners = await _api.GetMinersAsync();
            _dispatcher.Invoke(() =>
            {
                _index.Clear(); Miners.Clear();
                foreach (var m in miners) AddOrUpdate(m);
                RecalcFarmStats(); RebuildWatchdogFilters(); Alerts.SetMiners(Miners); Settings.SetMiners(Miners);
                Pools.RefreshMiners();
            });
            await _hub.ConnectAsync(ServerUrl, ApiKey);
            StatusMessage = _hub.IsConnected ? $"На связи · {Miners.Count} устройств" : $"Загружено устройств: {Miners.Count}";

            if (SelectedTab == 1) await Schedules.LoadAsync();
            if (SelectedTab == 2) await Alerts.LoadAsync();
            if (SelectedTab == 3) await Settings.LoadAsync();

            await RefreshBitcoinStatsAsync();
            await RefreshPingsAsync();
        }
        catch (Exception ex) { _log.LogError(ex, "Connect failed"); StatusMessage = $"Ошибка подключения: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!_api.IsConfigured) return;
        try
        {
            IsLoading = true;
            var miners = await _api.GetMinersAsync();
            _dispatcher.Invoke(() =>
            {
                _index.Clear(); Miners.Clear();
                foreach (var m in miners) AddOrUpdate(m);
                RecalcFarmStats(); RebuildWatchdogFilters(); Alerts.SetMiners(Miners); Settings.SetMiners(Miners);
                Pools.RefreshMiners();
            });
            StatusMessage = $"Данные обновлены: {Miners.Count}";
            await RefreshBitcoinStatsAsync();
            await RefreshPingsAsync();
        }
        catch (Exception ex) { StatusMessage = $"Ошибка синхронизации: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    private async Task RefreshBitcoinStatsAsync()
    {
        try
        {
            var stats = await _btc.GetStatsAsync();
            if (stats.BtcPriceUsd <= 0 && stats.NetworkDifficulty <= 0) return;
            _dispatcher.Invoke(() => { BtcPriceUsd = stats.BtcPriceUsd; NetworkDifficulty = stats.NetworkDifficulty; RecalcProfitability(); });
        }
        catch (Exception ex) { _log.LogWarning(ex, "RefreshBitcoinStats failed"); }
    }

    private void RecalcFarmStats()
    {
        var totalGhs = 0.0; var totalW = 0; var online = 0;
        foreach (var m in Miners) { totalGhs += m.HashrateGhs; totalW += m.PowerW; if (m.IsOnline) online++; }
        TotalHashrateGhs = totalGhs; TotalPowerW = totalW; MinerCount = Miners.Count; OnlineMinerCount = online; DailyPowerKwh = totalW / 1000.0 * 24.0;
        RecalcProfitability();
    }

    private void RecalcProfitability()
    {
        if (NetworkDifficulty > 0 && BtcPriceUsd > 0 && TotalHashrateGhs > 0)
        {
            var hashrateH = TotalHashrateGhs * 1e9;
            var dailyBtc = (decimal)((hashrateH / (NetworkDifficulty * TwoPow32)) * SecondsPerDay) * BlockRewardBtc;
            DailyBtcEstimate = dailyBtc; DailyUsdEstimate = dailyBtc * BtcPriceUsd;
        }
        else { DailyBtcEstimate = 0; DailyUsdEstimate = 0; }
        OnPropertyChanged(nameof(TotalHashrateDisplay)); OnPropertyChanged(nameof(TotalPowerDisplay)); OnPropertyChanged(nameof(DailyBtcDisplay)); OnPropertyChanged(nameof(DailyUsdDisplay)); OnPropertyChanged(nameof(BtcPriceDisplay)); OnPropertyChanged(nameof(DifficultyDisplay)); OnPropertyChanged(nameof(DailyPowerDisplay)); OnPropertyChanged(nameof(OnlineDisplay));
    }

    private void AddOrUpdate(MinerDto dto)
    {
        if (_index.TryGetValue(dto.MinerId, out var existing)) { existing.UpdateFrom(dto); return; }
        var vm = new MinerItemViewModel(dto, ExecuteCommandAsync, OpenDetailAsync, OpenSettingsAsync);
        _index[dto.MinerId] = vm; Miners.Add(vm);
    }

    private async Task ExecuteCommandAsync(string minerId, MinerCommandKind kind)
    {
        if (!_api.IsConfigured) { StatusMessage = "Сначала выполните подключение"; return; }
        StatusMessage = $"Отправка команды '{kind}' на {minerId}...";
        try { var result = await _api.SendCommandAsync(minerId, kind); StatusMessage = result.Success ? $"Команда '{kind}' выполнена на {minerId}" : $"Ошибка команды: {result.Error}"; }
        catch (Exception ex) { StatusMessage = $"Ошибка выполнения: {ex.Message}"; }
    }

    private async Task OpenDetailAsync(MinerItemViewModel vm)
    {
        if (!_api.IsConfigured) { StatusMessage = "Сначала выполните подключение"; return; }
        try
        {
            var detailLog = _loggerFactory.CreateLogger<MinerDetailViewModel>();
            var detailVm = new MinerDetailViewModel(_api, detailLog);
            var window = new MinerDetailWindow(detailVm) { Owner = Application.Current.MainWindow };
            _ = detailVm.LoadAsync(vm.MinerId, vm.Model, vm.Ip);
            window.ShowDialog();
        }
        catch (Exception ex) { _log.LogError(ex, "OpenDetail failed"); StatusMessage = $"Ошибка открытия деталей: {ex.Message}"; }
    }

    private async Task OpenSettingsAsync(MinerItemViewModel vm)
    {
        if (!_api.IsConfigured) { StatusMessage = "Сначала выполните подключение"; return; }
        try
        {
            var settingsVm = new MinerSettingsViewModel(_api, vm.MinerId);
            var window = new MinerSettingsWindow(settingsVm) { Owner = Application.Current.MainWindow };
            window.ShowDialog();
        }
        catch (Exception ex) { _log.LogError(ex, "OpenSettings failed"); StatusMessage = $"Ошибка открытия настроек: {ex.Message}"; }
    }

    private void OnMinerUpdated(MinerUpdate update)
    {
        _dispatcher.Invoke(() =>
        {
            if (_index.TryGetValue(update.MinerId, out var vm)) { vm.ApplyLiveUpdate(update); RecalcFarmStats(); return; }
            var dto = new MinerDto(update.MinerId, update.Ip, update.Mac, update.Hostname, update.Model, update.ModelCode, $"{update.FirmwareName} {update.FirmwareVersion}".Trim(), update.WatchdogId, update.Timestamp, update.Timestamp, null);
            var newVm = new MinerItemViewModel(dto, ExecuteCommandAsync, OpenDetailAsync, OpenSettingsAsync);
            newVm.ApplyLiveUpdate(update);
            _index[update.MinerId] = newVm; Miners.Add(newVm);
            Alerts.SetMiners(Miners); Settings.SetMiners(Miners); RecalcFarmStats(); RebuildWatchdogFilters();
            Pools.RefreshMiners();
            StatusMessage = $"Обнаружено новое устройство: {update.Model} ({update.Ip})";
        });
    }

    private void OnMinerRemoved(MinerRemoved removed)
    {
        _dispatcher.Invoke(() =>
        {
            if (!_index.TryGetValue(removed.MinerId, out var vm)) return;
            _index.Remove(removed.MinerId); Miners.Remove(vm);
            Alerts.SetMiners(Miners); Settings.SetMiners(Miners); RecalcFarmStats(); RebuildWatchdogFilters();
            Pools.RefreshMiners();
            StatusMessage = $"Устройство отключено ({removed.Reason}): {removed.MinerId}";
        });
    }

    public string TotalHashrateDisplay => TotalHashrateGhs switch { >= 1e9 => $"{TotalHashrateGhs / 1e9:F2} EH/s", >= 1e6 => $"{TotalHashrateGhs / 1e6:F2} PH/s", >= 1e3 => $"{TotalHashrateGhs / 1e3:F2} TH/s", _ => $"{TotalHashrateGhs:F2} GH/s" };
    public string TotalPowerDisplay => TotalPowerW >= 1000 ? $"{TotalPowerW / 1000.0:F2} кВт" : $"{TotalPowerW} Вт";
    public string DailyBtcDisplay => DailyBtcEstimate > 0 ? $"{DailyBtcEstimate:F8} BTC" : "0.00000000 BTC";
    public string DailyUsdDisplay => DailyUsdEstimate > 0 ? $"${DailyUsdEstimate:F2}" : "$0.00";
    public string BtcPriceDisplay => BtcPriceUsd > 0 ? $"${BtcPriceUsd:N0}" : "Загрузка...";
    public string DifficultyDisplay => NetworkDifficulty > 0 ? $"{NetworkDifficulty / 1e12:F2} T" : "Загрузка...";
    public string DailyPowerDisplay => $"{DailyPowerKwh:F1} кВт⋅ч";
    public string OnlineDisplay => $"{OnlineMinerCount} из {MinerCount}";
}