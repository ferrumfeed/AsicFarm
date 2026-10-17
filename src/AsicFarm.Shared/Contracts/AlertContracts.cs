namespace AsicFarm.Shared.Contracts;

public enum AlertRuleType
{
    MinerOffline,
    HashrateBelow,
    TempAbove,
    StateIs,
}

public enum AlertTargetKind
{
    All,
    Miner,
    Watchdog,
}

public sealed record AlertRuleDto(
    long Id,
    string Name,
    AlertRuleType Type,
    string Target,
    double Threshold,
    string? Extra,
    int ForSeconds,
    int CooldownSeconds,
    bool Enabled,
    string Channel,
    string? ChannelConfig,
    DateTimeOffset CreatedAt);

public sealed record AlertRuleCreateRequest(
    string Name,
    AlertRuleType Type,
    string? Target,
    double Threshold,
    string? Extra,
    int ForSeconds,
    int CooldownSeconds,
    bool Enabled,
    string? Channel,
    string? ChannelConfig);

public sealed record AlertEventDto(
    long Id,
    long RuleId,
    string RuleName,
    string MinerId,
    string MinerIp,
    DateTimeOffset FiredAt,
    string Message,
    bool Delivered,
    string? DeliveryError);