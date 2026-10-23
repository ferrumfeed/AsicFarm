using System.Windows;
using AsicFarm.Client.Api;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AsicFarm.Client.ViewModels;

public sealed partial class MinerSettingsViewModel : ObservableObject
{
    private readonly ServerApiClient _api;
    private readonly string _minerId;

    [ObservableProperty] private int _targetTemp = 65;
    [ObservableProperty] private string _coolingMode = "auto";
    [ObservableProperty] private int _fanDuty = 50;
    [ObservableProperty] private bool _quietMode = false;
    [ObservableProperty] private int _restartTemp = 90;
    [ObservableProperty] private int _startupDelay = 0;
    [ObservableProperty] private string _statusMessage = "";

    public MinerSettingsViewModel(ServerApiClient api, string minerId)
    {
        _api = api;
        _minerId = minerId;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            StatusMessage = "Сохранение температуры...";
            await _api.SetTargetTempAsync(_minerId, TargetTemp);

            StatusMessage = "Сохранение режима охлаждения...";
            await _api.SetCoolingModeAsync(_minerId, CoolingMode, FanDuty);

            StatusMessage = "Сохранение тихого режима...";
            await _api.SetQuietModeAsync(_minerId, QuietMode);

            StatusMessage = "Сохранение температуры рестарта...";
            await _api.SetRestartTempAsync(_minerId, RestartTemp);

            StatusMessage = "Сохранение задержки старта...";
            await _api.SetStartupDelayAsync(_minerId, StartupDelay);

            StatusMessage = "Настройки успешно сохранены!";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка: {ex.Message}";
        }
    }
}