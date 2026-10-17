using System.Collections.ObjectModel;
using System.Windows;
using AsicFarm.Client.Api;
using AsicFarm.Shared.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AsicFarm.Client.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ServerApiClient _api;
    private readonly ILogger<SettingsViewModel> _log;

    [ObservableProperty] private string _telegramChatId = "";
    [ObservableProperty] private bool _telegramConfigured;
    [ObservableProperty] private string _telegramStatus = "";
    [ObservableProperty] private bool _isEditing = true;

    public ObservableCollection<AutoActionDto> AutoActions { get; } = new();
    public ObservableCollection<AutoActionEventDto> AutoActionEvents { get; } = new();
    public ObservableCollection<MinerItemViewModel> Miners { get; } = new();

    [ObservableProperty] private string _newAutoName = "";
    [ObservableProperty] private AutoActionTrigger _newAutoTrigger = AutoActionTrigger.HashrateBelow;
    [ObservableProperty] private double _newAutoThreshold = 50;
    [ObservableProperty] private int _newAutoForSeconds = 180;
    [ObservableProperty] private AutoActionKind _newAutoAction = AutoActionKind.RestartMining;
    [ObservableProperty] private AlertTargetKind _newAutoTargetKind = AlertTargetKind.Watchdog;
    [ObservableProperty] private MinerItemViewModel? _selectedAutoMiner;
    [ObservableProperty] private string _newAutoWatchdogId = "";
    [ObservableProperty] private int _newAutoCooldownSeconds = 1800;
    [ObservableProperty] private bool _newAutoEnabled = true;

    public ObservableCollection<AutoActionTrigger> Triggers { get; } = new()
    {
        AutoActionTrigger.HashrateBelow,
        AutoActionTrigger.TempAbove,
        AutoActionTrigger.Offline,
    };

    public ObservableCollection<AutoActionKind> Actions { get; } = new()
    {
        AutoActionKind.RestartMining,
        AutoActionKind.Reboot,
        AutoActionKind.StopMining,
        AutoActionKind.PauseMining,
    };

    public ObservableCollection<AlertTargetKind> TargetKinds { get; } = new()
    {
        AlertTargetKind.Watchdog,
        AlertTargetKind.Miner,
    };

    // Для XAML: поле только для чтения, если не в режиме редактирования
    public bool IsReadOnly => !IsEditing;

    public SettingsViewModel(ServerApiClient api, ILogger<SettingsViewModel> log)
    {
        _api = api;
        _log = log;
    }

    public void SetMiners(IEnumerable<MinerItemViewModel> miners)
    {
        var list = miners.ToList();

        if (SelectedAutoMiner is not null && !list.Contains(SelectedAutoMiner))
            SelectedAutoMiner = null;

        Miners.Clear();
        foreach (var m in list) Miners.Add(m);

        if (SelectedAutoMiner is null && Miners.Count > 0)
            SelectedAutoMiner = Miners[0];

        if (string.IsNullOrWhiteSpace(NewAutoWatchdogId) && Miners.Count > 0)
            NewAutoWatchdogId = Miners[0].WatchdogId;
    }

    partial void OnIsEditingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsReadOnly));
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (!_api.IsConfigured)
        {
            TelegramStatus = "Сначала подключись к серверу";
            return;
        }

        try
        {
            var settingsTask = _api.GetSettingsAsync();
            var autoTask = _api.GetAutoActionsAsync();
            var eventsTask = _api.GetAutoActionEventsAsync(100);

            await Task.WhenAll(settingsTask, autoTask, eventsTask);

            var settings = await settingsTask;
            var autos = await autoTask;
            var events = await eventsTask;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                TelegramChatId = settings.TelegramChatId ?? "";
                TelegramConfigured = settings.TelegramConfigured;

                // Если ChatId уже сохранён — открываем в режиме "только чтение"
                IsEditing = string.IsNullOrWhiteSpace(TelegramChatId);

                if (!settings.BotTokenPresent)
                    TelegramStatus = "⚠ Бот не настроен на сервере";
                else if (!settings.TelegramConfigured)
                    TelegramStatus = "⚠ Не указан Chat ID";
                else
                    TelegramStatus = "✅ Уведомления включены";

                AutoActions.Clear();
                foreach (var a in autos) AutoActions.Add(a);

                AutoActionEvents.Clear();
                foreach (var e in events) AutoActionEvents.Add(e);
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Load settings failed");
            TelegramStatus = "Ошибка загрузки: " + ex.Message;
        }
    }

    /// Кнопка «Изменить» — разблокирует поле ChatId.
    [RelayCommand]
    private void BeginEdit()
    {
        IsEditing = true;
        TelegramStatus = "Измени Chat ID и нажми «Проверить»";
    }

    /// Кнопка «Отмена» — возвращает к сохранённому значению.
    [RelayCommand]
    private async Task CancelEditAsync()
    {
        IsEditing = false;
        await LoadAsync();
    }

    /// Кнопка «Сохранить» (показывается только в режиме редактирования).
    [RelayCommand]
    private async Task SaveTelegramAsync()
    {
        if (!_api.IsConfigured)
        {
            MessageBox.Show("Сначала подключись к серверу");
            return;
        }

        try
        {
            await _api.SaveSettingsAsync(new SettingsUpdateRequest
            {
                TelegramChatId = TelegramChatId,
            });

            TelegramConfigured = true;
            TelegramStatus = "✅ Сохранено";
            IsEditing = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Ошибка сохранения: " + ex.Message);
        }
    }

    /// Кнопка «Проверить» — сохраняет, потом отправляет тест.
    [RelayCommand]
    private async Task TestTelegramAsync()
    {
        if (!_api.IsConfigured)
        {
            MessageBox.Show("Сначала подключись к серверу.");
            return;
        }

        // Если в режиме редактирования — сначала сохраняем, потом проверяем.
        // Если уже сохранено — сразу проверяем.
        if (IsEditing)
        {
            try
            {
                await _api.SaveSettingsAsync(new SettingsUpdateRequest
                {
                    TelegramChatId = TelegramChatId,
                });

                TelegramConfigured = true;
                IsEditing = false;
            }
            catch (Exception ex)
            {
                TelegramStatus = "❌ Не удалось сохранить: " + ex.Message;
                return;
            }
        }

        TelegramStatus = "Отправка тестового сообщения...";
        try
        {
            var result = await _api.TestTelegramAsync();
            TelegramStatus = result.Ok
                ? "✅ Тестовое сообщение отправлено — загляни в Telegram"
                : "❌ " + (result.Error ?? "не удалось отправить");
        }
        catch (Exception ex)
        {
            TelegramStatus = "❌ " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task CreateAutoActionAsync()
    {
        if (!_api.IsConfigured)
        {
            MessageBox.Show("Сначала подключись к серверу.");
            return;
        }

        if (string.IsNullOrWhiteSpace(NewAutoName))
        {
            MessageBox.Show("Введи название действия");
            return;
        }

        var target = NewAutoTargetKind switch
        {
            AlertTargetKind.Miner => "Miner:" + (SelectedAutoMiner?.MinerId ?? ""),
            _ => "Watchdog:" + NewAutoWatchdogId,
        };

        if (NewAutoTargetKind == AlertTargetKind.Miner && SelectedAutoMiner is null)
        {
            MessageBox.Show("Выбери майнер");
            return;
        }

        try
        {
            await _api.CreateAutoActionAsync(new AutoActionCreateRequest(
                Name: NewAutoName,
                Trigger: NewAutoTrigger,
                Threshold: NewAutoThreshold,
                ForSeconds: NewAutoForSeconds,
                Action: NewAutoAction,
                Target: target,
                CooldownSeconds: NewAutoCooldownSeconds,
                Enabled: NewAutoEnabled));

            NewAutoName = "";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Ошибка: " + ex.Message);
        }
    }

    [RelayCommand]
    private async Task DeleteAutoActionAsync(AutoActionDto? a)
    {
        if (a is null || !_api.IsConfigured) return;
        if (MessageBox.Show($"Удалить '{a.Name}'?", "Подтверждение",
                MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

        try
        {
            await _api.DeleteAutoActionAsync(a.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Ошибка: " + ex.Message);
        }
    }

    [RelayCommand]
    private async Task ToggleAutoActionAsync(AutoActionDto? a)
    {
        if (a is null || !_api.IsConfigured) return;
        try
        {
            if (a.Enabled) await _api.DisableAutoActionAsync(a.Id);
            else await _api.EnableAutoActionAsync(a.Id);
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