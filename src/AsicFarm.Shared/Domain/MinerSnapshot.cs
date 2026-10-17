namespace AsicFarm.Shared.Domain;

public sealed record TempRange(int Min, int Max)
{
    public static readonly TempRange Empty = new(0, 0);
}

public sealed record ChipStatusCounts(int Red, int Orange, int Grey);

public sealed record ChainSnapshot(
    int Id,
    ChainState State,
    string? FailureMessage,
    double HashrateRealtimeGhs,
    double HashrateNominalGhs,
    double HashratePercent,
    double FrequencyMhz,
    int VoltageMv,
    int PowerConsumptionW,
    TempRange ChipTemp,
    TempRange PcbTemp,
    int? InletWaterTempC,
    int? OutletWaterTempC,
    int HwErrors,
    double HrError,
    ChipStatusCounts ChipStatuses);

public sealed record PoolSnapshot(
    int Id,
    string Url,
    string User,
    PoolKind Kind,
    PoolState Status,
    bool AsicBoost,
    string Difficulty,
    double DifficultyActual,
    int Accepted,
    int Rejected,
    int Stale,
    int PingMs,
    double LastShareDifficulty,
    string LastShareTime);

public sealed record FanSnapshot(int Id, int Rpm, int MaxRpm, FanState Status);

public sealed record CoolingSnapshot(
    int FanCount,
    int FanDutyPercent,
    FanControlMode Mode,
    IReadOnlyList<FanSnapshot> Fans);

public sealed record MinerSnapshot(
    string MinerId,
    string IpAddress,
    string MacAddress,
    DateTimeOffset Timestamp,
    FirmwareKind Firmware,

    MinerState State,
    string? StateDescription,
    int? FailureCode,
    TimeSpan? StateDuration,

    double HashrateRealtimeGhs,
    double HashrateAverageGhs,
    double HashrateNominalGhs,
    double HashrateStockGhs,
    double HashratePercent,

    int PowerConsumptionW,
    double PowerEfficiencyJPerTh,

    TempRange ChipTemp,
    TempRange PcbTemp,
    int? PsuTempLlc1,
    int? PsuTempLlc2,
    int? PsuTempPfc,

    int HwErrors,
    double HwErrorsPercent,
    double HrError,
    int FoundBlocks,
    long BestShare,
    double DevFeePercent,

    IReadOnlyList<ChainSnapshot> Chains,
    IReadOnlyList<PoolSnapshot> Pools,
    CoolingSnapshot Cooling,

    string Model,
    string ModelCode,
    string Algorithm,
    string FirmwareVersion,
    string FirmwareName,
    string SerialNumber,
    string Hostname,
    int? ChipsPerChain,
    int? NumChains);