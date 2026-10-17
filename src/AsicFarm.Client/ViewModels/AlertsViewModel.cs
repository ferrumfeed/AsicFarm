using System.Collections.ObjectModel;
using System.Windows;
using AsicFarm.Client.Api;
using AsicFarm.Shared.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AsicFarm.Client.ViewModels;

public sealed partial class AlertsViewModel : ObservableObject
{
    private readonly ServerApiClient _api;
    private readonly ILogger<AlertsViewModel> _log;

    public ObservableCollection<AlertRuleDto> Rules { get; } = new();
    public ObservableCollection<AlertEventDto> Events { get; } = new();
    public ObservableCollection<MinerItemViewModel> Miners { get; } = new();

    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private AlertRuleType _newType = AlertRuleType.HashrateBelow;
    [ObservableProperty] private AlertTargetKind _newTargetKind = AlertTargetKind.Watchdog;
    [ObservableProperty] private MinerItemViewModel? _selectedMiner;
    [ObservableProperty] private string _newWatchdogId = "";
    [ObservableProperty] private double _newThreshold = 90;
    [ObservableProperty] private string _newExtra = "";
    [ObservableProperty] private int _newForSeconds = 180;
    [ObservableProperty] private int _newCooldownSeconds = 900;
    [ObservableProperty] private bool _newEnabled = true;

    public ObservableCollection<AlertRuleType> AlertTypes { get; } = new()
    {
        AlertRuleType.MinerOffline,
        AlertRuleType.HashrateBelow,
        AlertRuleType.TempAbove,
        AlertRuleType.StateIs,
    };

    // Убрали All — пользователь работает только со своими майнерами
    public ObservableCollection<AlertTargetKind> TargetKinds { get; } = new()
    {
        AlertTargetKind.Watchdog,   // Все мои майнеры (по умолчанию)
        AlertTargetKind.Miner,      // Один майнер
    };

    public AlertsViewModel(ServerApiClient api, ILogger<AlertsViewModel> log)
    {
        _api = api;
        _log = log;
    }

    public void SetMiners(IEnumerable<MinerItemViewModel> miners)
    {
        var list = miners.ToList();

        if (SelectedMiner is not null && !list.Contains(SelectedMiner))
            SelectedMiner = null;

        Miners.Clear();
        foreach (var m in list) Miners.Add(m);

        if (SelectedMiner is null && Miners.Count > 0)
            SelectedMiner = Miners[0];

        if (string.IsNullOrWhiteSpace(NewWatchdogId) && Miners.Count > 0)
            NewWatchdogId = Miners[0].WatchdogId;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (!_api.IsConfigured) return;

        try
        {
            var rules = await _api.GetAlertRulesAsync();
            var events = await _api.GetAlertEventsAsync(100);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Rules.Clear();
                foreach (var r in rules) Rules.Add(r);

                Events.Clear();
                foreach (var e in events) Events.Add(e);
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Load alerts failed");
        }
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(NewName))
        {
            MessageBox.Show("Введи название уведомления");
            return;
        }

        var target = NewTargetKind switch
        {
            AlertTargetKind.Miner => "Miner:" + (SelectedMiner?.MinerId ?? ""),
            AlertTargetKind.Watchdog => "Watchdog:" + NewWatchdogId,
            _ => "Watchdog:" + NewWatchdogId,
        };

        if (NewTargetKind == AlertTargetKind.Miner && SelectedMiner is null)
        {
            MessageBox.Show("Выбери майнер");
            return;
        }

        try
        {
            var req = new AlertRuleCreateRequest(
                Name: NewName,
                Type: NewType,
                Target: target,
                Threshold: NewThreshold,
                Extra: string.IsNullOrWhiteSpace(NewExtra) ? null : NewExtra,
                ForSeconds: NewForSeconds,
                CooldownSeconds: NewCooldownSeconds,
                Enabled: NewEnabled,
                Channel: "Telegram",
                ChannelConfig: null);

            await _api.CreateAlertRuleAsync(req);
            NewName = "";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Ошибка: " + ex.Message);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(AlertRuleDto? r)
    {
        if (r is null) return;
        if (MessageBox.Show($"Удалить '{r.Name}'?", "Подтверждение",
                MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

        try
        {
            await _api.DeleteAlertRuleAsync(r.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Ошибка: " + ex.Message);
        }
    }

    [RelayCommand]
    private async Task ToggleAsync(AlertRuleDto? r)
    {
        if (r is null) return;
        try
        {
            if (r.Enabled) await _api.DisableAlertRuleAsync(r.Id);
            else await _api.EnableAlertRuleAsync(r.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Ошибка: " + ex.Message);
        }
    }

    [RelayCommand]
    private async Task RefreshEventsAsync() => await LoadAsync();
}