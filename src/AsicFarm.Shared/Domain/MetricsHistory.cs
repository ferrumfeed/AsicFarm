namespace AsicFarm.Shared.Domain;

public enum MinerEventKind { Start, Stop, Restart, Reboot, Overheat, DisableChain, EnableChain }

public sealed record MetricsPoint(
    DateTimeOffset Time,
    double HashrateGhs,
    int PcbMaxTempC,
    int ChipMaxTempC,
    int FanDutyPercent,
    int PowerConsumptionW);

public sealed record MinerEventAnnotation(
    DateTimeOffset Time,
    MinerEventKind Kind,
    int? ChainId);

public sealed record MetricsHistory(
    IReadOnlyList<MetricsPoint> Points,
    IReadOnlyList<MinerEventAnnotation> Annotations);