using System.Collections.ObjectModel;
using System.Windows;
using AsicFarm.Client.Api;
using AsicFarm.Shared.Vnish;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AsicFarm.Client.ViewModels;

public sealed partial class MinerSettingsViewModel : ObservableObject
{
    private readonly ServerApiClient _api;
    private readonly string _minerId;

    [ObservableProperty] private int _targetTemp = 65;
    [ObservableProperty] private bool _isSwitcherEnabled = true;
    [ObservableProperty] private string _minPreset = "2050";
    [ObservableProperty] private string _maxPreset = "4730";
    [ObservableProperty] private int _riseTemp = 66;
    [ObservableProperty] private int _decreaseTemp = 72;
    [ObservableProperty] private string _statusMessage = "";

    public ObservableCollection<VnishAutotunePreset> AvailablePresets { get; } = new();

    public MinerSettingsViewModel(ServerApiClient api, string minerId)
    {
        _api = api;
        _minerId = minerId;
        _ = LoadPresetsAsync();
    }

    private async Task LoadPresetsAsync()
    {
        try
        {
            var presets = await _api.GetPresetsAsync(_minerId);
            Application.Current.Dispatcher.Invoke(() =>
            {
                AvailablePresets.Clear();
                foreach (var p in presets) AvailablePresets.Add(p);
            });
        }
        catch (Exception ex) { StatusMessage = $"Пресеты не загружены: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Math.Abs(DecreaseTemp - RiseTemp) < 5)
        {
            StatusMessage = "Ошибка: разница между понижением и повышением должна быть не менее 5°C.";
            return;
        }

        try
        {
            await _api.SetTargetTempAsync(_minerId, TargetTemp);
            await _api.ConfigurePresetSwitcherAsync(_minerId, IsSwitcherEnabled, MinPreset, MaxPreset, RiseTemp, DecreaseTemp);
            StatusMessage = "Настройки отправлены на сервер.";
        }
        catch (Exception ex) { StatusMessage = $"Ошибка: {ex.Message}"; }
    }
}