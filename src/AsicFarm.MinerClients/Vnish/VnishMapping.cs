using AsicFarm.Shared.Domain;
using AsicFarm.Shared.Vnish;

namespace AsicFarm.MinerClients.Vnish;

internal static class VnishMapping
{
    public static MinerState ToMinerState(this string? s) => s switch
    {
        "mining" => MinerState.Mining,
        "initializing" => MinerState.Initializing,
        "starting" => MinerState.Starting,
        "auto-tuning" => MinerState.AutoTuning,
        "restarting" => MinerState.Restarting,
        "shutting-down" => MinerState.ShuttingDown,
        "stopped" => MinerState.Stopped,
        "failure" => MinerState.Failure,
        _ => MinerState.Unknown,
    };

    public static ChainState ToChainState(this string? s) => s switch
    {
        "initializing" => ChainState.Initializing,
        "mining" => ChainState.Mining,
        "stopped" => ChainState.Stopped,
        "failure" => ChainState.Failure,
        "disconnected" => ChainState.Disconnected,
        "disabled" => ChainState.Disabled,
        _ => ChainState.Unknown,
    };

    public static PoolState ToPoolState(this string? s) => s switch
    {
        "offline" => PoolState.Offline,
        "working" => PoolState.Working,
        "disabled" => PoolState.Disabled,
        "active" => PoolState.Active,
        "rejecting" => PoolState.Rejecting,
        _ => PoolState.Unknown,
    };

    public static PoolKind ToPoolKind(this string? s) => s switch
    {
        "UserPool" => PoolKind.UserPool,
        "DevFee" => PoolKind.DevFee,
        "Refund" => PoolKind.Refund,
        _ => PoolKind.UserPool,
    };

    public static FanState ToFanState(this string? s) =>
        s == "ok" ? FanState.Ok : FanState.Lost;

    public static FanControlMode ToFanMode(this string? s) => s switch
    {
        "auto" => FanControlMode.Auto,
        "manual" => FanControlMode.Manual,
        "immersion" => FanControlMode.Immersion,
        _ => FanControlMode.Auto,
    };

    public static MinerEventKind ToEventKind(this string? s) => s switch
    {
        "start" => MinerEventKind.Start,
        "stop" => MinerEventKind.Stop,
        "restart" => MinerEventKind.Restart,
        "reboot" => MinerEventKind.Reboot,
        "overheat" => MinerEventKind.Overheat,
        "disable_chain" => MinerEventKind.DisableChain,
        "enable_chain" => MinerEventKind.EnableChain,
        _ => MinerEventKind.Start,
    };

    public static ChipGrade ToChipGrade(this string? s) => s switch
    {
        "grey" => ChipGrade.Grey,
        "orange" => ChipGrade.Orange,
        "red" => ChipGrade.Red,
        _ => ChipGrade.Grey,
    };

    public static TempRange ToTempRange(this VnishTempRange r) => new(r.Min, r.Max);

    public static double ToGhs(this double value, string? measure) => measure switch
    {
        "GH/s" => value,
        "MH/s" => value / 1000.0,
        "TH/s" => value * 1000.0,
        _ => value,
    };

    public static ChainSnapshot ToChainSnapshot(this VnishAntmChain c) => new(
        Id: c.Id,
        State: c.Status.State.ToChainState(),
        FailureMessage: c.Status.Description,
        HashrateRealtimeGhs: c.HashrateRt,
        HashrateNominalGhs: c.HashrateIdeal,
        HashratePercent: c.HashratePercentage,
        FrequencyMhz: c.Frequency,
        VoltageMv: c.Voltage,
        PowerConsumptionW: c.PowerConsumption,
        ChipTemp: c.ChipTemp.ToTempRange(),
        PcbTemp: c.PcbTemp.ToTempRange(),
        InletWaterTempC: c.InletWaterTemp,
        OutletWaterTempC: c.OutletWaterTemp,
        HwErrors: c.HwErrors,
        HrError: c.HrError,
        ChipStatuses: new ChipStatusCounts(c.ChipStatuses.Red, c.ChipStatuses.Orange, c.ChipStatuses.Grey));

    public static PoolSnapshot ToPoolSnapshot(this VnishPoolStats p) => new(
        Id: p.Id,
        Url: p.Url,
        User: p.User,
        Kind: p.PoolType.ToPoolKind(),
        Status: p.Status.ToPoolState(),
        AsicBoost: p.AsicBoost,
        Difficulty: p.Diff,
        DifficultyActual: p.Diffa,
        Accepted: p.Accepted,
        Rejected: p.Rejected,
        Stale: p.Stale,
        PingMs: p.Ping,
        LastShareDifficulty: p.LsDiff,
        LastShareTime: p.LsTime);

    public static CoolingSnapshot ToCoolingSnapshot(this VnishCooling c) => new(
        FanCount: c.FanNum,
        FanDutyPercent: c.FanDuty,
        Mode: c.Settings.Mode.Name.ToFanMode(),
        Fans: c.Fans.Select(f => new FanSnapshot(f.Id, f.Rpm, f.MaxRpm, f.Status.ToFanState())).ToList());
}