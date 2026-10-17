using AsicFarm.Shared.Domain;

namespace AsicFarm.Shared.Contracts;

public sealed record FirmwareProbeResult(
    bool IsReachable,
    FirmwareKind Firmware,
    string? FirmwareVersion,
    string? Model,
    string? Error);

public sealed record FanLimits(int Min, int Max, int Default);

public sealed record OverclockLimits(
    int MinFreq, int MaxFreq, int DefaultFreq, int WarnFreq,
    int MinVolt, int MaxVolt, int DefaultVolt, int MaxVoltStockPsu);

public sealed record MinerIdentity(
    string MinerId,
    string Ip,
    string Mac,
    string Hostname,
    string Serial,
    string Model,
    string ModelCode,
    string Algorithm,
    string FirmwareName,
    string FirmwareVersion,
    string Platform,
    string Series,
    string InstallType,
    int? ChipsPerChain,
    int? NumChains,
    int? ChipsPerDomain,
    FanLimits? FanLimits,
    OverclockLimits? OverclockLimits);

public interface IAntminerClient : IAsyncDisposable
{
    string Host { get; }
    FirmwareKind Firmware { get; }

    Task<FirmwareProbeResult> ProbeAsync(CancellationToken ct);
    Task<MinerIdentity> GetIdentityAsync(CancellationToken ct);
    Task<MinerSnapshot> GetSnapshotAsync(CancellationToken ct);
    Task<MinerSnapshot> GetDetailedSnapshotAsync(CancellationToken ct);

    /// Полный детальный снапшот: агрегаты + почиповая инфа + топология + фабрика.
    /// Тянет /info, /status, /summary, /chains, /model, /chains/factory-info.
    /// Тяжёлый — вызывать редко (по запросу из UI, не в цикле мониторинга).
    Task<MinerDetailedSnapshot> GetFullDetailsAsync(CancellationToken ct);

    Task<MetricsHistory> GetMetricsAsync(TimeSpan window, TimeSpan step, CancellationToken ct);

    Task RebootAsync(CancellationToken ct);
    Task StartMiningAsync(CancellationToken ct);
    Task StopMiningAsync(CancellationToken ct);
    Task RestartMiningAsync(CancellationToken ct);
    Task PauseMiningAsync(CancellationToken ct);
    Task ResumeMiningAsync(CancellationToken ct);
    Task SwitchPoolAsync(int poolId, CancellationToken ct);
}