using System.Collections.ObjectModel;
using System.ComponentModel;
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
    public string DisplayName => Id == "" ? $"Все ватчдоги ({MinerCount})" : $"{Id} ({MinerCount})";
}

public sealed partial class MainViewModel : ObservableObject
{
    private const decimal BlockRewardBtc = 3.125m;
    private const double SecondsPerDay = 86400.0;
    private const double TwoPow32 = 4294967296.0;

    private readonly ServerApiClient _api;
    private readonly TelemetryHubClient _hub;
    private readonly BitcoinNetworkService _btc;
    private readonly ILogger<MainViewModel> _log;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<string, MinerItemViewModel> _index = new();
    private readonly ClientConfig _config;
    private readonly System.Timers.Timer _btcRefreshTimer;

    [ObservableProperty] private string _serverUrl = "http://localhost:5000";
    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _statusMessage = "Не подключено";
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

    public MainViewModel(
        ServerApiClient api, TelemetryHubClient hub, BitcoinNetworkService btc,
        ScheduleViewModel schedules, AlertsViewModel alerts, SettingsViewModel settings,
        PoolsViewModel pools, ILoggerFactory loggerFactory, ILogger<MainViewModel> log)
    {
        _api = api; _hub = hub; _btc = btc; Schedules = schedules; Alerts = alerts; Settings = settings; Pools = pools;
        _loggerFactory = loggerFactory; _log = log; _dispatcher = Dispatcher.CurrentDispatcher;

        Pools.SetMinerProvider(() => Miners.ToList());

        _config = ClientConfig.Load();
        _serverUrl = _config.ServerUrl; _apiKey = _config.ApiKey; _isDarkTheme = _config.IsDarkTheme; _viewMode = _config.ViewMode; _sortMode = _config.SortMode;

        MinersView = CollectionViewSource.GetDefaultView(Miners);
        ApplySorting(); ApplyFilter();

        _hub.MinerUpdated += OnMinerUpdated;
        _hub.MinerRemoved += OnMinerRemoved;
        _hub.ConnectionChanged += connected =>
        {
            _dispatcher.Invoke(() => { IsConnected = connected; StatusMessage = connected ? $"Live · подключено · {Miners.Count} майнер(ов)" : "Live · переподключение..."; });
        };

        _btcRefreshTimer = new System.Timers.Timer(TimeSpan.FromMinutes(5).TotalMilliseconds) { AutoReset = true };
        _btcRefreshTimer.Elapsed += async (_, _) => await RefreshBitcoinStatsAsync();
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
        WatchdogFilters.Add(new WatchdogFilterItem("", "Все ватчдоги", DateTimeOffset.MinValue, Miners.Count));
        var groups = Miners.GroupBy(m => m.WatchdogId).Select(g => new WatchdogFilterItem(g.Key, g.Key, g.Max(m => m.LastSeen), g.Count())).OrderByDescending(g => g.LastSeen);
        foreach (var g in groups) WatchdogFilters.Add(g);
        SelectedWatchdogFilter = WatchdogFilters.FirstOrDefault(f => f.Id == prev) ?? WatchdogFilters[0];
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerUrl) || string.IsNullOrWhiteSpace(ApiKey)) { StatusMessage = "Укажи URL сервера и API-ключ"; return; }
        _config.ServerUrl = ServerUrl; _config.ApiKey = ApiKey; _config.IsDarkTheme = IsDarkTheme; _config.ViewMode = ViewMode; _config.SortMode = SortMode; _config.ShowOnlyProblems = ShowOnlyProblems; _config.Save();

        IsLoading = true; StatusMessage = "Подключение...";
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
            StatusMessage = _hub.IsConnected ? $"Live · подключено · {Miners.Count} майнер(ов)" : $"Загружено: {Miners.Count} майнер(ов)";

            if (SelectedTab == 1) await Schedules.LoadAsync();
            if (SelectedTab == 2) await Alerts.LoadAsync();
            if (SelectedTab == 3) await Settings.LoadAsync();

            _btcRefreshTimer.Start();
            await RefreshBitcoinStatsAsync();
        }
        catch (Exception ex) { _log.LogError(ex, "Connect failed"); StatusMessage = $"Ошибка: {ex.Message}"; }
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
            StatusMessage = $"Обновлено: {Miners.Count}";
            await RefreshBitcoinStatsAsync();
        }
        catch (Exception ex) { StatusMessage = $"Ошибка обновления: {ex.Message}"; }
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
        if (!_api.IsConfigured) { StatusMessage = "Сначала подключись"; return; }
        StatusMessage = $"Команда {kind} → {minerId}...";
        try { var result = await _api.SendCommandAsync(minerId, kind); StatusMessage = result.Success ? $"OK: {kind} на {minerId}" : $"Ошибка: {result.Error}"; }
        catch (Exception ex) { StatusMessage = $"Ошибка: {ex.Message}"; }
    }

    private async Task OpenDetailAsync(MinerItemViewModel vm)
    {
        if (!_api.IsConfigured) { StatusMessage = "Сначала подключись"; return; }
        try
        {
            var detailLog = _loggerFactory.CreateLogger<MinerDetailViewModel>();
            var detailVm = new MinerDetailViewModel(_api, detailLog);
            var window = new MinerDetailWindow(detailVm) { Owner = Application.Current.MainWindow };
            _ = detailVm.LoadAsync(vm.MinerId, vm.Model, vm.Ip);
            window.ShowDialog();
        }
        catch (Exception ex) { _log.LogError(ex, "OpenDetail failed"); StatusMessage = $"Ошибка: {ex.Message}"; }
    }

    private async Task OpenSettingsAsync(MinerItemViewModel vm)
    {
        if (!_api.IsConfigured) { StatusMessage = "Сначала подключись"; return; }
        try
        {
            var settingsVm = new MinerSettingsViewModel(_api, vm.MinerId);
            var window = new MinerSettingsWindow(settingsVm) { Owner = Application.Current.MainWindow };
            window.ShowDialog();
        }
        catch (Exception ex) { _log.LogError(ex, "OpenSettings failed"); StatusMessage = $"Ошибка: {ex.Message}"; }
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
            StatusMessage = $"Новый майнер: {update.Model} · {update.Ip}";
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
            StatusMessage = $"Майнер удалён ({removed.Reason}): {removed.MinerId}";
        });
    }

    public string TotalHashrateDisplay => TotalHashrateGhs switch { >= 1e9 => $"{TotalHashrateGhs / 1e9:F2} EH/s", >= 1e6 => $"{TotalHashrateGhs / 1e6:F2} PH/s", >= 1e3 => $"{TotalHashrateGhs / 1e3:F2} TH/s", _ => $"{TotalHashrateGhs:F2} GH/s" };
    public string TotalPowerDisplay => TotalPowerW >= 1000 ? $"{TotalPowerW / 1000.0:F2} kW" : $"{TotalPowerW} W";
    public string DailyBtcDisplay => DailyBtcEstimate > 0 ? $"{DailyBtcEstimate:F8} BTC" : "—";
    public string DailyUsdDisplay => DailyUsdEstimate > 0 ? $"${DailyUsdEstimate:F2}" : "—";
    public string BtcPriceDisplay => BtcPriceUsd > 0 ? $"${BtcPriceUsd:N0}" : "—";
    public string DifficultyDisplay => NetworkDifficulty > 0 ? $"{NetworkDifficulty / 1e12:F2} T" : "—";
    public string DailyPowerDisplay => $"{DailyPowerKwh:F1} kWh";
    public string OnlineDisplay => $"{OnlineMinerCount} / {MinerCount}";
}