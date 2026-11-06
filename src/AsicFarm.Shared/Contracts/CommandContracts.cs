using AsicFarm.Shared.Domain;

namespace AsicFarm.Shared.Contracts;

public enum MinerCommandKind
{
    Reboot, StartMining, StopMining, RestartMining, PauseMining, ResumeMining, SwitchPool,
    RequestDetailedSnapshot, SetPools, SetTargetTemp, SetCoolingMode, SetQuietMode, SetRestartTemp, SetStartupDelay,
    ConfigurePresetSwitcher, GetAutotunePresets, GetSettings,
    GetRawSettings // <--- NEW
}

public sealed record MinerCommand(
    string RequestId, string MinerId, MinerCommandKind Kind,
    int? PoolId = null, List<PoolConfigDto>? Pools = null, int? TargetTemp = null,
    string? CoolingMode = null, int? FanDuty = null, bool? Enabled = null, int? Value = null,
    string? MinPreset = null, string? MaxPreset = null, int? RiseTemp = null, int? DecreaseTemp = null
);

public sealed record MinerCommandResult(
    string RequestId, string MinerId, bool Success, string? Error, DateTimeOffset CompletedAt, string? PayloadJson = null
);

public sealed record CommandResultDto(
    string RequestId, string MinerId, bool Success, string? Error, DateTimeOffset CompletedAt
);

public sealed record MinerSettingsDto(
    int TargetTemp, string CoolingMode, int FanDuty, bool QuietMode, int RestartTemp, int StartupDelay,
    bool PresetSwitcherEnabled, string? MinPreset, string? MaxPreset, int RiseTemp, int DecreaseTemp
);