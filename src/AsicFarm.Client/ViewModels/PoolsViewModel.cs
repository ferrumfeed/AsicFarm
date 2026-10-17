using System.Collections.ObjectModel;
using System.Windows;
using AsicFarm.Client.Api;
using AsicFarm.Shared.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AsicFarm.Client.ViewModels;

public enum SuffixMode { Index, IpLastOctet }

public sealed partial class PoolEntry : ObservableObject
{
    [ObservableProperty] private string _url = "";
    [ObservableProperty] private string _password = "";
}

public sealed partial class MinerSelectionItem : ObservableObject
{
    public MinerItemViewModel Miner { get; }
    [ObservableProperty] private bool _isSelected;
    public MinerSelectionItem(MinerItemViewModel miner) => Miner = miner;
}

public sealed partial class PoolsViewModel : ObservableObject
{
    private readonly ServerApiClient _api;
    private Func<List<MinerItemViewModel>> _getMiners = () => new();

    [ObservableProperty] private string _baseLogin = "";
    [ObservableProperty] private string _poolPassword = "x";
    [ObservableProperty] private SuffixMode _workerSuffixMode = SuffixMode.Index;

    public ObservableCollection<PoolEntry> Pools { get; } = new()
    {
        new PoolEntry { Url = "stratum+tcp://btc.k8s.com:3333" },
        new PoolEntry { Url = "" },
        new PoolEntry { Url = "" }
    };

    public ObservableCollection<MinerSelectionItem> Miners { get; } = new();

    public PoolsViewModel(ServerApiClient api) => _api = api;

    public void SetMinerProvider(Func<List<MinerItemViewModel>> provider)
    {
        _getMiners = provider;
        RefreshMiners();
    }

    public void RefreshMiners()
    {
        var currentIds = Miners.Select(m => m.Miner.MinerId).ToHashSet();
        var newMiners = _getMiners();

        for (int i = Miners.Count - 1; i >= 0; i--)
            if (!newMiners.Any(m => m.MinerId == Miners[i].Miner.MinerId)) Miners.RemoveAt(i);

        foreach (var m in newMiners)
            if (!currentIds.Contains(m.MinerId)) Miners.Add(new MinerSelectionItem(m));
    }

    [RelayCommand]
    private async Task ApplyToSelectedAsync()
    {
        if (string.IsNullOrWhiteSpace(BaseLogin)) { MessageBox.Show("Введите базовый логин."); return; }
        var selectedMiners = Miners.Where(m => m.IsSelected).Select(m => m.Miner).ToList();
        if (selectedMiners.Count == 0) { MessageBox.Show("Выберите майнеры."); return; }

        if (MessageBox.Show($"Применить пулы к {selectedMiners.Count} майнерам?", "Подтверждение", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

        int successCount = 0, errorCount = 0;
        string lastError = "";

        foreach (var (miner, index) in selectedMiners.Select((m, i) => (m, i)))
        {
            string suffix = WorkerSuffixMode switch
            {
                SuffixMode.IpLastOctet when miner.Ip.Contains('.') => miner.Ip.Split('.')[3],
                _ => (index + 1).ToString()
            };
            var workerName = $"{BaseLogin}.{suffix}";

            var poolConfigs = new List<PoolConfigDto>();
            foreach (var p in Pools)
                poolConfigs.Add(string.IsNullOrWhiteSpace(p.Url) ? new PoolConfigDto("", "", "") : new PoolConfigDto(p.Url, workerName, string.IsNullOrWhiteSpace(p.Password) ? PoolPassword : p.Password));

            try { await _api.SetPoolsAsync(miner.MinerId, poolConfigs); successCount++; }
            catch (Exception ex) { errorCount++; lastError = ex.Message; }
        }

        MessageBox.Show($"Готово!\nУспешно: {successCount}\nОшибок: {errorCount}\n{lastError}", "Результат");
    }
}