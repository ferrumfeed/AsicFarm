namespace AsicFarm.Shared.Contracts;

public enum AutoActionTrigger
{
    HashrateBelow,
    TempAbove,
    Offline,
}

public enum AutoActionKind
{
    Reboot,
    RestartMining,
    StopMining,
    PauseMining,
}

public sealed record AutoActionDto(
    long Id,
    string Name,
    AutoActionTrigger Trigger,
    double Threshold,
    int ForSeconds,
    AutoActionKind Action,
    string Target,
    int CooldownSeconds,
    bool Enabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastFiredAt);

public sealed record AutoActionCreateRequest(
    string Name,
    AutoActionTrigger Trigger,
    double Threshold,
    int ForSeconds,
    AutoActionKind Action,
    string? Target,
    int CooldownSeconds,
    bool Enabled);

public sealed record AutoActionEventDto(
    long Id,
    long RuleId,
    string RuleName,
    string MinerId,
    string MinerIp,
    string Action,
    DateTimeOffset FiredAt,
    bool Success,
    string? Error);