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
    private Func<List<MinerItemViewModel>> _getMiners = () => new();

    public ObservableCollection<AutoActionDto> AutoActions { get; } = new();
    public ObservableCollection<MinerItemViewModel> Miners { get; } = new();

    // Свойства для создания нового действия
    [ObservableProperty] private string _newAutoName = "";

    // ИСПОЛЬЗУЕМ ЕНУМЫ ВМЕСТО СТРОК
    [ObservableProperty] private AutoActionTrigger _newAutoTrigger = AutoActionTrigger.HashrateBelow;
    [ObservableProperty] private AutoActionKind _newAutoAction = AutoActionKind.Reboot;

    [ObservableProperty] private double _newAutoThreshold = 50;
    [ObservableProperty] private int _newAutoForSeconds = 180;
    [ObservableProperty] private int _newAutoCooldownSeconds = 1800;
    [ObservableProperty] private bool _newAutoEnabled = true;

    // Коллекции для ComboBox теперь содержат енумы
    public ObservableCollection<AutoActionTrigger> Triggers { get; } = new(Enum.GetValues<AutoActionTrigger>());
    public ObservableCollection<AutoActionKind> Actions { get; } = new(Enum.GetValues<AutoActionKind>());

    // Свойства для Telegram (ты их не менял, но я оставлю для полноты)
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
            var list = await _api.GetAutoActionsAsync();
            Application.Current.Dispatcher.Invoke(() =>
            {
                AutoActions.Clear();
                foreach (var item in list) AutoActions.Add(item);
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка загрузки авто-действий: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task CreateAutoActionAsync()
    {
        try
        {
            // ТЕПЕРЬ ТИПЫ СОВПАДАЮТ
            var req = new AutoActionCreateRequest(
                Name: NewAutoName,
                Trigger: NewAutoTrigger,
                Threshold: NewAutoThreshold,
                ForSeconds: NewAutoForSeconds,
                Action: NewAutoAction,
                Target: "All", // Если Target тоже енум, замени на AutoActionTarget.All
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