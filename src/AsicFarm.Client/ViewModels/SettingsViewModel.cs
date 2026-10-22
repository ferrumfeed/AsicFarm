using System.Collections.ObjectModel;
using System.Windows;
using AsicFarm.Client.Api;
using AsicFarm.Shared.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AsicFarm.Client.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ServerApiClient _api;

    // Для сохранения исходного значения при редактировании
    private string _originalChatId = "";

    // --- Автоматические действия ---
    public ObservableCollection<AutoActionDto> AutoActions { get; } = new();
    public ObservableCollection<MinerItemViewModel> Miners { get; } = new();

    [ObservableProperty] private string _newAutoName = "";
    [ObservableProperty] private AutoActionTrigger _newAutoTrigger = AutoActionTrigger.HashrateBelow;
    [ObservableProperty] private AutoActionKind _newAutoAction = AutoActionKind.Reboot;
    [ObservableProperty] private double _newAutoThreshold = 50;
    [ObservableProperty] private int _newAutoForSeconds = 180;
    [ObservableProperty] private int _newAutoCooldownSeconds = 1800;
    [ObservableProperty] private bool _newAutoEnabled = true;

    public ObservableCollection<AutoActionTrigger> Triggers { get; } = new(Enum.GetValues<AutoActionTrigger>());
    public ObservableCollection<AutoActionKind> Actions { get; } = new(Enum.GetValues<AutoActionKind>());

    // --- Telegram ---
    [ObservableProperty] private string _telegramChatId = "";
    [ObservableProperty] private string _telegramStatus = "";
    [ObservableProperty] private bool _isReadOnly = true;
    [ObservableProperty] private bool _isEditing = false;

    public SettingsViewModel(ServerApiClient api)
    {
        _api = api;
    }

    public void SetMiners(ObservableCollection<MinerItemViewModel> miners)
    {
        Miners.Clear();
        foreach (var m in miners) Miners.Add(m);
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            // 1. Загрузка настроек Telegram
            var settings = await _api.GetSettingsAsync();
            TelegramChatId = settings.TelegramChatId ?? "";

            if (settings.TelegramConfigured)
                TelegramStatus = "Telegram настроен и готов к отправке.";
            else
                TelegramStatus = "Telegram не настроен (укажите Chat ID).";

            // 2. Загрузка автоматических действий
            var list = await _api.GetAutoActionsAsync();
            Application.Current.Dispatcher.Invoke(() =>
            {
                AutoActions.Clear();
                foreach (var item in list) AutoActions.Add(item);
            });
        }
        catch (Exception ex)
        {
            TelegramStatus = $"Ошибка загрузки: {ex.Message}";
        }
    }

    // ==================== ЛОГИКА TELEGRAM ====================

    [RelayCommand]
    private void BeginEdit()
    {
        _originalChatId = TelegramChatId; // Запоминаем, чтобы можно было отменить
        IsEditing = true;
        IsReadOnly = false;
        TelegramStatus = "Режим редактирования. Внесите изменения и нажмите Сохранить.";
    }

    [RelayCommand]
    private void CancelEdit()
    {
        TelegramChatId = _originalChatId; // Возвращаем как было
        IsEditing = false;
        IsReadOnly = true;
        TelegramStatus = "Изменения отменены.";
    }

    [RelayCommand]
    private async Task SaveTelegramAsync()
    {
        try
        {
            await _api.SaveSettingsAsync(new SettingsUpdateRequest { TelegramChatId = TelegramChatId });
            IsEditing = false;
            IsReadOnly = true;
            TelegramStatus = "Настройки Telegram успешно сохранены!";
        }
        catch (Exception ex)
        {
            TelegramStatus = $"Ошибка сохранения: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task TestTelegramAsync()
    {
        try
        {
            TelegramStatus = "Отправка тестового сообщения...";
            var result = await _api.TestTelegramAsync();

            if (result.Ok)
                TelegramStatus = "✅ Тестовое сообщение успешно отправлено в Telegram!";
            else
                TelegramStatus = $"❌ Ошибка отправки: {result.Error}";
        }
        catch (Exception ex)
        {
            TelegramStatus = $"❌ Ошибка: {ex.Message}";
        }
    }

    // ==================== ЛОГИКА АВТОДЕЙСТВИЙ ====================

    [RelayCommand]
    private async Task CreateAutoActionAsync()
    {
        try
        {
            var req = new AutoActionCreateRequest(
                Name: NewAutoName,
                Trigger: NewAutoTrigger,
                Threshold: NewAutoThreshold,
                ForSeconds: NewAutoForSeconds,
                Action: NewAutoAction,
                Target: "All",
                CooldownSeconds: NewAutoCooldownSeconds,
                Enabled: NewAutoEnabled
            );

            await _api.CreateAutoActionAsync(req);
            NewAutoName = "";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка создания: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ToggleAutoActionAsync(AutoActionDto? action)
    {
        if (action == null) return;
        try
        {
            if (action.Enabled) await _api.DisableAutoActionAsync(action.Id);
            else await _api.EnableAutoActionAsync(action.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task DeleteAutoActionAsync(AutoActionDto? action)
    {
        if (action == null) return;
        if (MessageBox.Show($"Удалить правило '{action.Name}'?", "Подтверждение", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

        try
        {
            await _api.DeleteAutoActionAsync(action.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка удаления: {ex.Message}");
        }
    }
}