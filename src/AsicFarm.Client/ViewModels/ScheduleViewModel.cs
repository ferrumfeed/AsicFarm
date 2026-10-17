using System.Collections.ObjectModel;
using System.Windows;
using AsicFarm.Client.Api;
using AsicFarm.Shared.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AsicFarm.Client.ViewModels;

public sealed partial class ActionItem : ObservableObject
{
    [ObservableProperty] private string _time = "08:00";
    [ObservableProperty] private ScheduleAction _action = ScheduleAction.StartMining;

    public ActionItem() { }
    public ActionItem(DayAction a) { Time = a.Time; Action = a.Action; }

    public DayAction ToDto(DayOfWeek day) => new(day, Time, Action);
}

public sealed partial class DayPlanItem : ObservableObject
{
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private DayOfWeek _day;

    public ObservableCollection<ActionItem> Actions { get; } = new();

    public string DayName => Day switch
    {
        DayOfWeek.Monday => "Понедельник",
        DayOfWeek.Tuesday => "Вторник",
        DayOfWeek.Wednesday => "Среда",
        DayOfWeek.Thursday => "Четверг",
        DayOfWeek.Friday => "Пятница",
        DayOfWeek.Saturday => "Суббота",
        DayOfWeek.Sunday => "Воскресенье",
        _ => "?",
    };

    public DayPlanItem(DayOfWeek day)
    {
        Day = day;
        Actions.Add(new ActionItem { Time = "08:00", Action = ScheduleAction.StartMining });
        Actions.Add(new ActionItem { Time = "22:00", Action = ScheduleAction.StopMining });
    }

    public void SetActions(IEnumerable<DayAction> actions)
    {
        Actions.Clear();
        foreach (var a in actions) Actions.Add(new ActionItem(a));
    }
}

public sealed partial class ScheduleViewModel : ObservableObject
{
    private readonly ServerApiClient _api;
    private readonly ILogger<ScheduleViewModel> _log;
    private Func<List<MinerItemViewModel>> _getMiners;

    public ObservableCollection<ScheduleDto> Schedules { get; } = new();
    public ObservableCollection<MinerItemViewModel> Miners { get; } = new();
    public ObservableCollection<DayPlanItem> DayPlans { get; } = new();

    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private ScheduleTargetKind _newTargetKind = ScheduleTargetKind.Watchdog;
    [ObservableProperty] private MinerItemViewModel? _selectedMiner;
    [ObservableProperty] private string _newWatchdogId = "";
    [ObservableProperty] private bool _newEnabled = true;
    [ObservableProperty] private string _newTimeZone = "Europe/Moscow";

    // Убрали All
    public ObservableCollection<ScheduleTargetKind> TargetKinds { get; } = new()
    {
        ScheduleTargetKind.Watchdog,
        ScheduleTargetKind.Miner,
    };

    public ObservableCollection<ScheduleAction> AllActions { get; } = new()
    {
        ScheduleAction.StartMining,
        ScheduleAction.StopMining,
        ScheduleAction.RestartMining,
        ScheduleAction.Reboot,
    };

    public ObservableCollection<string> TimeZones { get; } = new()
    {
        "UTC",
        "Europe/Kaliningrad", "Europe/Moscow", "Europe/Samara",
        "Asia/Yekaterinburg", "Asia/Omsk", "Asia/Krasnoyarsk",
        "Asia/Irkutsk", "Asia/Yakutsk", "Asia/Vladivostok",
        "Asia/Magadan", "Asia/Kamchatka",
        "Asia/Almaty", "Asia/Tbilisi", "Asia/Baku", "Asia/Yerevan",
        "Europe/Minsk", "Europe/Kyiv",
        "Europe/Berlin", "Europe/London",
        "Asia/Dubai", "Asia/Shanghai",
        "America/New_York",
    };

    public ScheduleViewModel(
        ServerApiClient api,
        Func<List<MinerItemViewModel>> getMiners,
        ILogger<ScheduleViewModel> log)
    {
        _api = api;
        _getMiners = getMiners;
        _log = log;

        foreach (DayOfWeek d in Enum.GetValues<DayOfWeek>())
            DayPlans.Add(new DayPlanItem(d));
    }

    public void SetMinerProvider(Func<List<MinerItemViewModel>> provider) => _getMiners = provider;

    [RelayCommand]
    public async Task LoadAsync()
    {
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Miners.Clear();
            foreach (var m in _getMiners()) Miners.Add(m);

            if (SelectedMiner is null && Miners.Count > 0)
                SelectedMiner = Miners[0];

            if (string.IsNullOrWhiteSpace(NewWatchdogId) && Miners.Count > 0)
                NewWatchdogId = Miners[0].WatchdogId;
        });

        try
        {
            var list = await _api.GetSchedulesAsync();
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Schedules.Clear();
                foreach (var s in list) Schedules.Add(s);
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Load schedules failed");
        }
    }

    [RelayCommand]
    private void AddAction(DayPlanItem? day)
    {
        if (day is null) return;
        day.Enabled = true;
        day.Actions.Add(new ActionItem { Time = "12:00", Action = ScheduleAction.None });
    }

    [RelayCommand]
    private void RemoveAction(ActionItem? item)
    {
        if (item is null) return;

        foreach (var p in DayPlans)
        {
            if (p.Actions.Contains(item))
            {
                p.Actions.Remove(item);
                return;
            }
        }
    }

    [RelayCommand]
    private void PresetWeekdays()
    {
        foreach (var p in DayPlans)
        {
            var weekday = p.Day is >= DayOfWeek.Monday and <= DayOfWeek.Friday;
            p.Enabled = weekday;
            if (weekday)
            {
                p.SetActions(new[]
                {
                    new DayAction(p.Day, "08:00", ScheduleAction.StartMining),
                    new DayAction(p.Day, "22:00", ScheduleAction.StopMining),
                });
            }
        }
    }

    [RelayCommand]
    private void PresetWeekends()
    {
        foreach (var p in DayPlans)
        {
            var weekend = p.Day is DayOfWeek.Saturday or DayOfWeek.Sunday;
            p.Enabled = weekend;
            if (weekend)
            {
                p.SetActions(new[]
                {
                    new DayAction(p.Day, "10:00", ScheduleAction.StartMining),
                    new DayAction(p.Day, "20:00", ScheduleAction.StopMining),
                });
            }
        }
    }

    [RelayCommand]
    private void PresetEveryDay()
    {
        foreach (var p in DayPlans)
        {
            p.Enabled = true;
            p.SetActions(new[]
            {
                new DayAction(p.Day, "08:00", ScheduleAction.StartMining),
                new DayAction(p.Day, "22:00", ScheduleAction.StopMining),
            });
        }
    }

    [RelayCommand]
    private void PresetClear()
    {
        foreach (var p in DayPlans)
        {
            p.Enabled = false;
            p.Actions.Clear();
            p.Actions.Add(new ActionItem { Time = "08:00", Action = ScheduleAction.StartMining });
            p.Actions.Add(new ActionItem { Time = "22:00", Action = ScheduleAction.StopMining });
        }
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(NewName))
        {
            MessageBox.Show("Введи название расписания");
            return;
        }

        if (NewTargetKind == ScheduleTargetKind.Miner && SelectedMiner is null)
        {
            MessageBox.Show("Выбери майнер");
            return;
        }

        var actions = DayPlans
            .Where(p => p.Enabled && p.Actions.Count > 0)
            .SelectMany(p => p.Actions.Select(a => a.ToDto(p.Day)))
            .ToList();

        if (actions.Count == 0)
        {
            MessageBox.Show("Отметь хотя бы один день и добавь действия");
            return;
        }

        var targetId = NewTargetKind switch
        {
            ScheduleTargetKind.Miner => SelectedMiner!.MinerId,
            _ => NewWatchdogId,
        };

        try
        {
            var req = new ScheduleCreateRequest(
                Name: NewName,
                TargetKind: NewTargetKind,
                TargetId: targetId,
                Enabled: NewEnabled,
                TimeZoneId: NewTimeZone,
                Actions: actions);

            await _api.CreateScheduleAsync(req);

            NewName = "";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Ошибка создания: " + ex.Message);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(ScheduleDto? s)
    {
        if (s is null) return;
        if (MessageBox.Show($"Удалить '{s.Name}'?", "Подтверждение",
                MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

        try
        {
            await _api.DeleteScheduleAsync(s.Id);

            // ИСПРАВЛЕНИЕ: Мгновенно удаляем из коллекции для обновления UI
            Schedules.Remove(s);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Ошибка удаления: " + ex.Message);
        }
    }

    [RelayCommand]
    private async Task ToggleAsync(ScheduleDto? s)
    {
        if (s is null) return;
        try
        {
            if (s.Enabled) await _api.DisableScheduleAsync(s.Id);
            else await _api.EnableScheduleAsync(s.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Ошибка: " + ex.Message);
        }
    }
}