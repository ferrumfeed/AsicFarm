using AsicFarm.Shared.Domain;

namespace AsicFarm.Shared.Contracts;

public enum MinerCommandKind
{
    Reboot,
    StartMining,
    StopMining,
    RestartMining,
    PauseMining,
    ResumeMining,
    SwitchPool,
    RequestDetailedSnapshot,
    SetPools,
    SetTargetTemp,
    SetCoolingMode,
    SetQuietMode,
    SetRestartTemp,
    SetStartupDelay
}

public sealed record MinerCommand(
    string RequestId,
    string MinerId,
    MinerCommandKind Kind,
    int? PoolId = null,
    List<PoolConfigDto>? Pools = null,
    int? TargetTemp = null,
    string? CoolingMode = null,
    int? FanDuty = null,
    bool? Enabled = null,
    int? Value = null
);

public sealed record MinerCommandResult(
    string RequestId,
    string MinerId,
    bool Success,
    string? Error,
    DateTimeOffset CompletedAt);

public sealed record CommandResultDto(
    string RequestId,
    string MinerId,
    bool Success,
    string? Error,
    DateTimeOffset CompletedAt);