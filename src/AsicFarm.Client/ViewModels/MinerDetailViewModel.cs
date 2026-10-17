using System.Collections.ObjectModel;
using AsicFarm.Client.Api;
using AsicFarm.Shared.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace AsicFarm.Client.ViewModels;

public sealed class ChipCellVM
{
    public int Id { get; init; }
    public int ChainId { get; init; }
    public double TempC { get; init; }
    public int FrequencyMhz { get; init; }
    public int VoltageMv { get; init; }
    public int Errors { get; init; }
    public string Grade { get; init; } = "grey";
    public bool Throttled { get; init; }

    public string Color => TempC switch
    {
        <= 0 => "#3a3f4a",
        < 45 => "#2d7ff9",
        < 55 => "#3fb950",
        < 65 => "#d29922",
        < 75 => "#f0883e",
        _ => "#f85149",
    };

    public string Tooltip => $"Чип #{Id} (цепь {ChainId})\n" +
                             $"Температура: {TempC:F1}°C\n" +
                             $"Частота: {FrequencyMhz} МГц\n" +
                             $"Напряжение: {VoltageMv} мВ\n" +
                             $"Ошибки: {Errors}\n" +
                             $"Grade: {Grade}\n" +
                             (Throttled ? "⚠ Throttled" : "");
}

public sealed class ChainDetailVM
{
    public int Id { get; init; }
    public string StateText { get; init; } = "";
    public string StateColor { get; init; } = "#6b7280";
    public double HashrateGhs { get; init; }
    public double HashrateNominalGhs { get; init; }
    public double HashratePercent { get; init; }
    public double FrequencyMhz { get; init; }
    public string? FailureMessage { get; init; }

    public string HashrateText => HashrateGhs >= 1000
        ? $"{HashrateGhs / 1000.0:F1} TH/s"
        : $"{HashrateGhs:F0} GH/s";

    public string NominalText => HashrateNominalGhs >= 1000
        ? $"{HashrateNominalGhs / 1000.0:F1} TH/s"
        : $"{HashrateNominalGhs:F0} GH/s";

    public ObservableCollection<ChipCellVM> Cells { get; } = new();

    public int Cols { get; init; } = 1;
    public int Rows { get; init; } = 1;

    public List<List<ChipCellVM>> RowsData
    {
        get
        {
            var result = new List<List<ChipCellVM>>();
            for (var r = 0; r < Rows; r++)
            {
                var row = new List<ChipCellVM>();
                for (var c = 0; c < Cols; c++)
                {
                    var idx = r * Cols + c;
                    if (idx < Cells.Count) row.Add(Cells[idx]);
                }
                if (row.Count > 0) result.Add(row);
            }
            return result;
        }
    }
}

public sealed partial class MinerDetailViewModel : ObservableObject
{
    private readonly ServerApiClient _api;
    private readonly ILogger _log;

    [ObservableProperty] private string _minerId = "";
    [ObservableProperty] private string _title = "Детали майнера";
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _loadingText = "Запрос детального снапшота...";
    [ObservableProperty] private string? _error;
    [ObservableProperty] private bool _hasError;

    [ObservableProperty] private string _modelText = "";
    [ObservableProperty] private string _stateText = "";
    [ObservableProperty] private string _stateColor = "#6b7280";
    [ObservableProperty] private string _hashrateText = "—";
    [ObservableProperty] private string _tempText = "—";
    [ObservableProperty] private string _powerText = "—";
    [ObservableProperty] private string _efficiencyText = "—";
    [ObservableProperty] private string _firmwareText = "";
    [ObservableProperty] private string _uptimeText = "";

    [ObservableProperty] private bool _hasFactory;
    [ObservableProperty] private string _psuModelText = "—";
    [ObservableProperty] private string _psuSerialText = "—";
    [ObservableProperty] private string _stockHashrateText = "—";

    public ObservableCollection<FanSnapshot> Fans { get; } = new();
    [ObservableProperty] private int _fanDutyPercent;
    [ObservableProperty] private string _fanModeText = "—";

    public ObservableCollection<ChainDetailVM> Chains { get; } = new();

    public MinerDetailViewModel(ServerApiClient api, ILogger<MinerDetailViewModel> log)
    {
        _api = api;
        _log = log;
    }

    public async Task LoadAsync(string minerId, string model, string ip)
    {
        MinerId = minerId;
        Title = $"{model} · {ip}";

        IsLoading = true;
        HasError = false;
        Error = null;
        LoadingText = "Запрос детального снапшота у ватчдога...";

        try
        {
            LoadingText = "Ватчдог опрашивает асик (может занять 30-60 секунд)...";
            var snap = await _api.GetDetailedSnapshotAsync(minerId);

            if (snap is null)
            {
                Error = "Пустой ответ от сервера";
                HasError = true;
                return;
            }

            Apply(snap);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Load detail failed for {Miner}", minerId);
            Error = ex.Message;
            HasError = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Apply(MinerDetailedSnapshot snap)
    {
        var s = snap.Summary;

        ModelText = $"{s.Model} · {s.IpAddress}";
        StateText = RuState(s.State);
        StateColor = s.State switch
        {
            MinerState.Mining => "#5cb85c",
            MinerState.AutoTuning => "#5bc0de",
            MinerState.Restarting => "#f0ad4e",
            MinerState.Failure => "#d9534f",
            _ => "#9aa0a6",
        };

        HashrateText = s.HashrateRealtimeGhs >= 1000
            ? $"{s.HashrateRealtimeGhs / 1000.0:F2} TH/s ({s.HashratePercent:F1}%)"
            : $"{s.HashrateRealtimeGhs:F0} GH/s";

        TempText = $"{s.ChipTemp.Min}…{s.ChipTemp.Max}°C чип / {s.PcbTemp.Min}…{s.PcbTemp.Max}°C плата";
        PowerText = $"{s.PowerConsumptionW} Вт";
        EfficiencyText = $"{s.PowerEfficiencyJPerTh:F1} Дж/TH";
        FirmwareText = $"{s.FirmwareName} {s.FirmwareVersion}";
        UptimeText = s.StateDuration is { } d && d.TotalMinutes > 0
            ? $"в работе {Human(d)}"
            : "";

        FanDutyPercent = s.Cooling.FanDutyPercent;
        FanModeText = s.Cooling.Mode switch
        {
            FanControlMode.Auto => "Авто",
            FanControlMode.Manual => "Ручной",
            FanControlMode.Immersion => "Иммерсия",
            _ => "—",
        };
        Fans.Clear();
        foreach (var f in s.Cooling.Fans)
            Fans.Add(f);

        HasFactory = snap.Factory is not null;
        if (snap.Factory is { } fac)
        {
            PsuModelText = string.IsNullOrWhiteSpace(fac.PsuModel) ? "—" : fac.PsuModel;
            PsuSerialText = string.IsNullOrWhiteSpace(fac.PsuSerial) ? "—" : fac.PsuSerial;
            StockHashrateText = fac.StockHashrateGhs is { } stock && stock > 0
                ? (stock >= 1000 ? $"{stock / 1000.0:F1} TH/s" : $"{stock:F0} GH/s")
                : "—";
        }

        Chains.Clear();

        var topologyByChain = snap.Topologies.ToDictionary(t => t.ChainId);

        foreach (var cd in snap.ChainDetails)
        {
            var stateText = RuChainState(cd.State);
            var stateColor = cd.State switch
            {
                ChainState.Mining => "#5cb85c",
                ChainState.Failure => "#d9534f",
                ChainState.Disabled => "#6b7280",
                _ => "#f0ad4e",
            };

            var hrPercent = cd.HashrateNominalGhs > 0
                ? cd.HashrateRealtimeGhs / cd.HashrateNominalGhs * 100.0
                : 0.0;

            var chipsById = cd.Chips.ToDictionary(c => c.Id);

            var cells = new List<ChipCellVM>();
            int rows = 1, cols = cd.Chips.Count;

            if (topologyByChain.TryGetValue(cd.ChainId, out var topo) &&
                topo.Matrix.Count > 0)
            {
                cols = topo.Matrix[0].Count;
                rows = topo.Matrix.Count;

                foreach (var row in topo.Matrix)
                {
                    foreach (var chipId in row)
                    {
                        if (chipsById.TryGetValue(chipId, out var chip))
                            cells.Add(ToCell(chip));
                        else
                            cells.Add(new ChipCellVM { Id = chipId });
                    }
                }
            }
            else
            {
                foreach (var chip in cd.Chips.OrderBy(c => c.Id))
                    cells.Add(ToCell(chip));
            }

            var vm = new ChainDetailVM
            {
                Id = cd.ChainId,
                StateText = stateText,
                StateColor = stateColor,
                HashrateGhs = cd.HashrateRealtimeGhs,
                HashrateNominalGhs = cd.HashrateNominalGhs,
                HashratePercent = hrPercent,
                FrequencyMhz = cd.FrequencyMhz,
                FailureMessage = cd.FailureMessage,
                Cols = cols,
                Rows = rows,
            };

            foreach (var cell in cells)
                vm.Cells.Add(cell);

            Chains.Add(vm);
        }
    }

    private static ChipCellVM ToCell(ChipInfo c) => new()
    {
        Id = c.Id,
        ChainId = c.ChainId,
        TempC = c.TempC,
        FrequencyMhz = c.FrequencyMhz,
        VoltageMv = c.VoltageMv,
        Errors = c.Errors,
        Grade = c.Grade.ToString().ToLowerInvariant(),
        Throttled = c.Throttled,
    };

    private static string RuState(MinerState s) => s switch
    {
        MinerState.Mining => "Майнит",
        MinerState.Initializing => "Инициализация",
        MinerState.Starting => "Запуск",
        MinerState.AutoTuning => "Автотюнинг",
        MinerState.Restarting => "Перезапуск",
        MinerState.ShuttingDown => "Остановка",
        MinerState.Stopped => "Остановлен",
        MinerState.Failure => "Ошибка",
        _ => "—",
    };

    private static string RuChainState(ChainState s) => s switch
    {
        ChainState.Mining => "Майнит",
        ChainState.Initializing => "Инициализация",
        ChainState.Stopped => "Остановлена",
        ChainState.Failure => "Ошибка",
        ChainState.Disconnected => "Отключена",
        ChainState.Disabled => "Отключена",
        _ => "—",
    };

    private static string Human(TimeSpan t)
    {
        if (t.TotalMinutes < 60) return $"{(int)t.TotalMinutes} мин";
        if (t.TotalHours < 24) return $"{(int)t.TotalHours} ч {(int)(t.TotalMinutes % 60)} мин";
        return $"{(int)t.TotalDays} д {t.Hours} ч";
    }
}