using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using AsicFarm.Client.Api;
using AsicFarm.Client.Services;
using AsicFarm.Shared.Contracts;
using AsicFarm.Shared.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AsicFarm.Client.ViewModels;

public sealed class ChainRowVM
{
    public int Id { get; init; }
    public double HashrateGhs { get; init; }
    public double HashratePercent { get; init; }
    public int ChipTempMin { get; init; }
    public int ChipTempMax { get; init; }
    public int HwErrors { get; init; }

    public string HashrateText => HashrateGhs >= 1000 ? $"{HashrateGhs / 1000.0:F1} TH/s" : $"{HashrateGhs:F0} GH/s";
    public string TempText => ChipTempMax > 0 ? $"{ChipTempMin}–{ChipTempMax}°C" : "—";
    public string TempColor => ChipTempMax switch { <= 0 => "#6b7280", < 60 => "#5cb85c", < 70 => "#f0ad4e", < 78 => "#fd7e14", _ => "#d9534f" };
}

public sealed class PoolRowVM
{
    public int Id { get; init; }
    public string Url { get; init; } = "";
    public string StatusText { get; init; } = "";
    public int Accepted { get; init; }
    public int Rejected { get; init; }
    public int PingMs { get; init; }
    public string StatusColor => StatusText switch { "Активен" or "Работает" => "#5cb85c", "Отклоняет" => "#f0ad4e", "Отключён" => "#6b7280", _ => "#6b7280" };
}

public sealed partial class MinerItemViewModel : ObservableObject
{
    private readonly Func<string, MinerCommandKind, Task>? _commandHandler;
    private readonly Func<MinerItemViewModel, Task>? _openDetailHandler;
    private readonly Func<MinerItemViewModel, Task>? _openSettingsHandler;

    public string MinerId { get; }

    [ObservableProperty] private string _ip = "";
    [ObservableProperty] private string _mac = "";
    [ObservableProperty] private string _hostname = "";
    [ObservableProperty] private string _model = "";
    [ObservableProperty] private string _firmware = "";
    [ObservableProperty] private string _watchdogId = "";
    [ObservableProperty] private string _state = "Unknown";

    [ObservableProperty] private string _workerName = "—";
    [ObservableProperty] private double _hashrateGhs;
    [ObservableProperty] private double _hashratePercent;
    [ObservableProperty] private int _chipMaxTemp;
    [ObservableProperty] private int _pcbMaxTemp;
    [ObservableProperty] private int _powerW;
    [ObservableProperty] private double _powerEfficiencyJPerTh;
    [ObservableProperty] private DateTimeOffset _lastSeen;
    [ObservableProperty] private bool _isOnline;
    [ObservableProperty] private bool _isExecuting;

    [ObservableProperty] private string _uptimeText = "";
    [ObservableProperty] private string _efficiencyText = "";
    [ObservableProperty] private string _bestShareText = "";
    [ObservableProperty] private string _foundBlocksText = "";
    [ObservableProperty] private string _hwErrorsText = "";
    [ObservableProperty] private string _devFeeText = "";
    [ObservableProperty] private string _fansText = "";
    [ObservableProperty] private string _psuTempsText = "";

    public ObservableCollection<ChainRowVM> Chains { get; } = new();
    public ObservableCollection<PoolRowVM> Pools { get; } = new();

    public string DisplayName => string.IsNullOrEmpty(Ip) ? Model : $"{Model} · {Ip}";

    public int ProblemScore
    {
        get
        {
            var s = 0;
            if (!IsOnline) s += 500;
            s += State switch { "Failure" => 300, "Stopped" => 100, "Restarting" => 80, "AutoTuning" => 40, _ => 0 };
            if (HashratePercent > 0 && HashratePercent < 80) s += 150;
            else if (HashratePercent > 0 && HashratePercent < 90) s += 80;
            else if (HashratePercent > 0 && HashratePercent < 95) s += 30;
            if (ChipMaxTemp > 80) s += 200;
            else if (ChipMaxTemp > 75) s += 100;
            else if (ChipMaxTemp > 70) s += 40;
            return s;
        }
    }

    public bool HasProblem => !IsOnline || State is "Failure" or "Stopped" || (HashratePercent > 0 && HashratePercent < 90) || ChipMaxTemp > 75;

    public string HashrateDisplay => HashrateGhs > 0 ? $"{HashrateGhs / 1000.0:F2} TH/s" : "—";
    public string EfficiencyDisplay => PowerEfficiencyJPerTh > 0 ? $"{PowerEfficiencyJPerTh:F1} Дж/TH" : "—";
    public string TempDisplay => ChipMaxTemp > 0 ? $"{ChipMaxTemp}°C" : "—";
    public string BoardTempDisplay => PcbMaxTemp > 0 ? $"{PcbMaxTemp}°C" : "—";
    public string HashratePercentDisplay => HashratePercent > 0 ? $"{HashratePercent:F1}%" : "—";

    public string StateColor => State switch { "Mining" => "#5cb85c", "AutoTuning" => "#5bc0de", "Restarting" => "#f0ad4e", "Failure" => "#d9534f", "Stopped" => "#9aa0a6", _ => "#9aa0a6" };
    public string HashrateBarColor => HashratePercent switch { <= 0 => "#6b7280", >= 95 => "#5cb85c", >= 85 => "#f0ad4e", _ => "#d9534f" };
    public string HashrateColor => HashratePercent switch { <= 0 => "#6b7280", >= 95 => "#5cb85c", >= 85 => "#f0ad4e", _ => "#d9534f" };
    public string TempColor => ChipMaxTemp switch { <= 0 => "#6b7280", < 60 => "#5cb85c", < 70 => "#f0ad4e", < 78 => "#fd7e14", _ => "#d9534f" };
    public string PcbTempColor => PcbMaxTemp switch { <= 0 => "#6b7280", < 50 => "#5cb85c", < 60 => "#f0ad4e", < 70 => "#fd7e14", _ => "#d9534f" };
    public string EfficiencyColor => PowerEfficiencyJPerTh switch { <= 0 => "#6b7280", < 28 => "#5cb85c", < 32 => "#f0ad4e", < 36 => "#fd7e14", _ => "#d9534f" };

    public MinerItemViewModel(
        MinerDto dto,
        Func<string, MinerCommandKind, Task>? commandHandler = null,
        Func<MinerItemViewModel, Task>? openDetailHandler = null,
        Func<MinerItemViewModel, Task>? openSettingsHandler = null)
    {
        MinerId = dto.MinerId;
        _commandHandler = commandHandler;
        _openDetailHandler = openDetailHandler;
        _openSettingsHandler = openSettingsHandler;
        UpdateFrom(dto);
    }

    partial void OnIsExecutingChanged(bool value)
    {
        RebootCommand.NotifyCanExecuteChanged(); StopMiningCommand.NotifyCanExecuteChanged();
        StartMiningCommand.NotifyCanExecuteChanged(); RestartMiningCommand.NotifyCanExecuteChanged();
    }

    private bool CanExecuteAction() => !IsExecuting;

    public void UpdateFrom(MinerDto dto)
    {
        Ip = dto.Ip; Mac = dto.Mac; Hostname = dto.Hostname; Model = dto.Model; Firmware = dto.Firmware; WatchdogId = dto.WatchdogId; LastSeen = dto.LastSeen;
        var age = DateTimeOffset.UtcNow - dto.LastSeen; IsOnline = age < TimeSpan.FromMinutes(2);
        if (dto.Snapshot is { } snap) ApplySnapshot(snap);
        OnPropertyChanged(nameof(DisplayName)); OnPropertyChanged(nameof(ProblemScore)); OnPropertyChanged(nameof(HasProblem)); OnPropertyChanged(nameof(StateColor));
    }

    public void ApplyLiveUpdate(MinerUpdate update)
    {
        State = update.State; HashrateGhs = update.HashrateGhs; HashratePercent = update.HashratePercent; ChipMaxTemp = update.ChipMaxTemp; PowerW = update.PowerW; LastSeen = update.Timestamp; IsOnline = true;
        OnPropertyChanged(nameof(ProblemScore)); OnPropertyChanged(nameof(HasProblem)); OnPropertyChanged(nameof(HashrateDisplay)); OnPropertyChanged(nameof(HashratePercentDisplay)); OnPropertyChanged(nameof(TempDisplay)); OnPropertyChanged(nameof(HashrateColor)); OnPropertyChanged(nameof(TempColor)); OnPropertyChanged(nameof(StateColor));
    }

    private void ApplySnapshot(MinerSnapshot snap)
    {
        State = snap.State.ToString(); HashrateGhs = snap.HashrateRealtimeGhs; HashratePercent = snap.HashratePercent; ChipMaxTemp = snap.ChipTemp?.Max ?? 0; PcbMaxTemp = snap.PcbTemp?.Max ?? 0; PowerW = snap.PowerConsumptionW; PowerEfficiencyJPerTh = snap.PowerEfficiencyJPerTh;

        var activePool = snap.Pools.FirstOrDefault(p => !string.IsNullOrEmpty(p.User));
        if (activePool != null) { var userParts = activePool.User.Split('.'); WorkerName = userParts.Length > 1 ? userParts.Last() : activePool.User; }
        else if (!string.IsNullOrEmpty(snap.Hostname)) WorkerName = snap.Hostname;

        UptimeText = snap.StateDuration is { } dur && dur.TotalMinutes > 0 ? $"в работе {Human(dur)}" : "";
        EfficiencyText = snap.PowerEfficiencyJPerTh > 0 ? $"{snap.PowerEfficiencyJPerTh:F1} Дж/TH" : "";
        BestShareText = snap.BestShare > 0 ? $"Лучший шар: {FormatBig(snap.BestShare)}" : "";
        FoundBlocksText = snap.FoundBlocks > 0 ? $"Блоков найдено: {snap.FoundBlocks}" : "Блоков найдено: 0";
        HwErrorsText = snap.HwErrorsPercent > 0 ? $"HW-ошибки: {snap.HwErrorsPercent:F3}%" : "HW-ошибки: 0";
        DevFeeText = snap.DevFeePercent > 0 ? $"DevFee: {snap.DevFeePercent:F2}%" : "";

        var psuParts = new List<string>();
        if (snap.PsuTempLlc1 is { } l1) psuParts.Add($"LLC1 {l1}°C");
        if (snap.PsuTempLlc2 is { } l2) psuParts.Add($"LLC2 {l2}°C");
        if (snap.PsuTempPfc is { } pfc) psuParts.Add($"PFC {pfc}°C");
        PsuTempsText = psuParts.Count > 0 ? "PSU: " + string.Join(" · ", psuParts) : "";

        if (snap.Cooling is { } cool && cool.Fans.Count > 0) FansText = $"Вентиляторы: {string.Join(" / ", cool.Fans.Select(f => f.Rpm))} об/мин · ШИМ {cool.FanDutyPercent}%";
        else FansText = "";

        Chains.Clear();
        foreach (var c in snap.Chains.OrderBy(x => x.Id)) Chains.Add(new ChainRowVM { Id = c.Id, HashrateGhs = c.HashrateRealtimeGhs, HashratePercent = c.HashratePercent, ChipTempMin = c.ChipTemp?.Min ?? 0, ChipTempMax = c.ChipTemp?.Max ?? 0, HwErrors = c.HwErrors });

        Pools.Clear();
        foreach (var p in snap.Pools.OrderBy(x => x.Id).Where(p => p.Status == PoolState.Active || p.Status == PoolState.Working))
            Pools.Add(new PoolRowVM { Id = p.Id, Url = p.Url, StatusText = MapPoolStatus(p.Status), Accepted = p.Accepted, Rejected = p.Rejected, PingMs = p.PingMs });

        OnPropertyChanged(nameof(HashrateDisplay)); OnPropertyChanged(nameof(EfficiencyDisplay)); OnPropertyChanged(nameof(TempDisplay)); OnPropertyChanged(nameof(BoardTempDisplay)); OnPropertyChanged(nameof(HashratePercentDisplay)); OnPropertyChanged(nameof(HashrateColor)); OnPropertyChanged(nameof(TempColor)); OnPropertyChanged(nameof(PcbTempColor)); OnPropertyChanged(nameof(EfficiencyColor)); OnPropertyChanged(nameof(ProblemScore)); OnPropertyChanged(nameof(HasProblem));
    }

    private static string MapPoolStatus(PoolState s) => s switch { PoolState.Active => "Активен", PoolState.Working => "Работает", PoolState.Rejecting => "Отклоняет", PoolState.Disabled => "Отключён", PoolState.Offline => "Офлайн", _ => "—" };
    private static string Human(TimeSpan t) => t.TotalMinutes < 60 ? $"{(int)t.TotalMinutes} мин" : t.TotalHours < 24 ? $"{(int)t.TotalHours} ч {(int)(t.TotalMinutes % 60)} мин" : $"{(int)t.TotalDays} д {t.Hours} ч";
    private static string FormatBig(long n) => n >= 1_000_000_000 ? $"{n / 1_000_000_000.0:F2}B" : n >= 1_000_000 ? $"{n / 1_000_000.0:F2}M" : n >= 1_000 ? $"{n / 1_000.0:F2}K" : n.ToString();

    [RelayCommand] private async Task OpenDetailsAsync() { if (_openDetailHandler is not null) await _openDetailHandler(this); }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        if (_openSettingsHandler is not null) await _openSettingsHandler(this);
    }

    [RelayCommand]
    private void OpenWebUi()
    {
        if (string.IsNullOrWhiteSpace(Ip)) return;
        try { Process.Start(new ProcessStartInfo($"http://{Ip}") { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show($"Не удалось открыть браузер: {ex.Message}"); }
    }

    private async Task ExecuteCommandAsync(MinerCommandKind kind)
    {
        if (_commandHandler is null) return;
        IsExecuting = true;
        try
        {
            var commandTask = _commandHandler(MinerId, kind);
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(15));
            var completedTask = await Task.WhenAny(commandTask, timeoutTask);

            if (completedTask == timeoutTask) MessageBox.Show($"Майнер {Model} ({Ip}) не ответил вовремя.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            else await commandTask;
        }
        catch (Exception ex) { MessageBox.Show($"Ошибка при отправке команды: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { IsExecuting = false; }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteAction))] private Task RebootAsync() => ExecuteCommandAsync(MinerCommandKind.Reboot);
    [RelayCommand(CanExecute = nameof(CanExecuteAction))] private Task StopMiningAsync() => ExecuteCommandAsync(MinerCommandKind.StopMining);
    [RelayCommand(CanExecute = nameof(CanExecuteAction))] private Task StartMiningAsync() => ExecuteCommandAsync(MinerCommandKind.StartMining);
    [RelayCommand(CanExecute = nameof(CanExecuteAction))] private Task RestartMiningAsync() => ExecuteCommandAsync(MinerCommandKind.RestartMining);
}