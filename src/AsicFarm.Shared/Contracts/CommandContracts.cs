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
    ConfigurePresetSwitcher,
    GetPresets
}

public sealed record MinerCommand(
    string RequestId,
    string MinerId,
    MinerCommandKind Kind,
    int? PoolId = null,
    List<PoolConfigDto>? Pools = null,
    int? TargetTemp = null,
    ConfigurePresetSwitcherRequest? PresetSwitcher = null
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