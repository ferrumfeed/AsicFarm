namespace AsicFarm.Shared.Domain;

/// Данные одного чипа в цепочке.
public sealed record ChipInfo(
    int Id,             // номер чипа в цепочке (0..75)
    int ChainId,        // какая цепь (1..3)
    double HashrateGhs,
    int FrequencyMhz,
    int VoltageMv,
    double TempC,
    int Errors,
    ChipGrade Grade,
    bool Throttled);

/// Топология цепочки: матрица чипов (row, col) → id чипа.
public sealed record ChainTopology(
    int ChainId,
    int Rows,
    int Cols,
    IReadOnlyList<IReadOnlyList<int>> Matrix);

/// Информация о чипах одной цепи.
public sealed record ChainChipsInfo(
    int ChainId,
    ChainState State,
    string? FailureMessage,
    double HashrateRealtimeGhs,
    double HashrateNominalGhs,
    double FrequencyMhz,
    IReadOnlyList<ChipInfo> Chips,
    IReadOnlyList<ChipSensorInfo> Sensors);

/// Состояние температурного сенсора чипа.
public sealed record ChipSensorInfo(
    int BoardId,
    int ChipId,
    int Location,
    string State);

/// Фабричная инфа по цепи.
public sealed record FactoryChainInfo(
    int Id,
    string BoardModel,
    string Serial,
    int ChipBin,
    int FrequencyMhz,
    int VoltageMv,
    double HashrateGhs);

/// Полная фабричная информация о майнере.
public sealed record FactoryInfo(
    IReadOnlyList<FactoryChainInfo> Chains,
    double? StockHashrateGhs,
    string? PsuModel,
    string? PsuSerial,
    bool? HasPics);

/// Полный детальный снапшот: агрегаты + чипы + топология + фабрика.
public sealed record MinerDetailedSnapshot(
    string MinerId,
    DateTimeOffset Timestamp,
    MinerSnapshot Summary,
    IReadOnlyList<ChainChipsInfo> ChainDetails,
    IReadOnlyList<ChainTopology> Topologies,
    FactoryInfo? Factory);