using System.Collections.ObjectModel;
using AsicFarm.Client.Api;
using AsicFarm.Shared.Contracts;
using AsicFarm.Shared.Vnish;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AsicFarm.Client.ViewModels;

// Изменено с record на class для стабильной работы ComboBox в WPF
public sealed class PresetItem
{
    public string Name { get; }
    public string Pretty { get; }

    public PresetItem(string name, string pretty)
    {
        Name = name;
        Pretty = pretty;
    }

    public override string ToString() => Pretty;
}

public sealed partial class MinerSettingsViewModel : ObservableObject
{
    private readonly ServerApiClient _api;
    private readonly string _minerId;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusMessage = "";

    // ===== Существующие настройки =====
    [ObservableProperty] private int _targetTemp = 65;
    [ObservableProperty] private string _coolingMode = "auto";
    [ObservableProperty] private int _fanDuty = 50;
    [ObservableProperty] private bool _quietMode = false;
    [ObservableProperty] private int _restartTemp = 90;
    [ObservableProperty] private int _startupDelay = 0;

    // ===== Переключатель пресетов =====
    [ObservableProperty] private bool _presetSwitcherEnabled;
    [ObservableProperty] private PresetItem? _selectedMinPreset;
    [ObservableProperty] private PresetItem? _selectedMaxPreset;
    [ObservableProperty] private int _riseTemp = 67;
    [ObservableProperty] private int _decreaseTemp = 73;

    // ===== UI логика =====
    public bool IsManualCooling => CoolingMode == "manual";

    public ObservableCollection<PresetItem> AvailablePresets { get; } = new();
    public string Title => $"Настройки · {_minerId}";

    public MinerSettingsViewModel(ServerApiClient api, string minerId)
    {
        _api = api;
        _minerId = minerId;
        _ = LoadDataAsync();
    }

    partial void OnCoolingModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsManualCooling));
    }

    public async Task LoadDataAsync()
    {
        IsLoading = true;
        StatusMessage = "Загрузка настроек...";
        try
        {
            var settings = await _api.GetMinerSettingsAsync(_minerId);
            if (settings != null)
            {
                TargetTemp = settings.TargetTemp;
                CoolingMode = settings.CoolingMode;
                FanDuty = settings.FanDuty;
                QuietMode = settings.QuietMode;
                RestartTemp = settings.RestartTemp;
                StartupDelay = settings.StartupDelay;
                PresetSwitcherEnabled = settings.PresetSwitcherEnabled;
                RiseTemp = settings.RiseTemp;
                DecreaseTemp = settings.DecreaseTemp;
            }

            var presets = await _api.GetPresetsAsync(_minerId);
            AvailablePresets.Clear();
            foreach (var p in presets)
            {
                if (string.IsNullOrWhiteSpace(p.Name)) continue;

                // ИСКЛЮЧАЕМ ДЕФОЛТНЫЙ ПРЕСЕТ
                if (p.Name.Equals("default", StringComparison.OrdinalIgnoreCase)) continue;

                var pretty = string.IsNullOrWhiteSpace(p.Pretty) ? p.Name : p.Pretty;
                AvailablePresets.Add(new PresetItem(p.Name, pretty));
            }

            if (settings != null)
            {
                SelectedMinPreset = AvailablePresets.FirstOrDefault(x => x.Name == settings.MinPreset);
                SelectedMaxPreset = AvailablePresets.FirstOrDefault(x => x.Name == settings.MaxPreset);
            }

            if (SelectedMinPreset == null && AvailablePresets.Count > 0) SelectedMinPreset = AvailablePresets[0];
            if (SelectedMaxPreset == null && AvailablePresets.Count > 0) SelectedMaxPreset = AvailablePresets[^1];

            StatusMessage = "Настройки загружены";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка загрузки: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            StatusMessage = "Сохранение...";
            await _api.SetTargetTempAsync(_minerId, TargetTemp);
            await _api.SetCoolingModeAsync(_minerId, CoolingMode, FanDuty);
            await _api.SetQuietModeAsync(_minerId, QuietMode);
            await _api.SetRestartTempAsync(_minerId, RestartTemp);
            await _api.SetStartupDelayAsync(_minerId, StartupDelay);
            StatusMessage = "Настройки успешно сохранены!";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ApplyPresetSwitcherAsync()
    {
        if (SelectedMinPreset == null || SelectedMaxPreset == null)
        {
            StatusMessage = "Выберите мин. и макс. пресеты";
            return;
        }

        if (SelectedMinPreset.Name.Equals("default", StringComparison.OrdinalIgnoreCase) ||
            SelectedMaxPreset.Name.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "VNISH не позволяет использовать дефолтный пресет в переключателе";
            return;
        }

        // VNISH: Температура понижения (decrease) должна быть выше температуры повышения (rise) минимум на 5°C
        if (DecreaseTemp < RiseTemp + 5)
        {
            StatusMessage = "Темп. понижения должна быть выше темп. повышения минимум на 5°C";
            return;
        }

        try
        {
            StatusMessage = "Применение переключателя...";
            await _api.ConfigurePresetSwitcherAsync(
                _minerId, PresetSwitcherEnabled,
                SelectedMinPreset.Name, SelectedMaxPreset.Name,
                RiseTemp, DecreaseTemp);

            StatusMessage = PresetSwitcherEnabled ? "Переключатель включён" : "Переключатель выключен";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка: {ex.Message}";
        }
    }
}