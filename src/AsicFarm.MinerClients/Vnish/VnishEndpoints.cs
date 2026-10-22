namespace AsicFarm.MinerClients.Vnish;

internal static class VnishEndpoints
{
    public const string Unlock = "unlock";
    public const string AuthCheck = "auth-check";
    public const string Info = "info";
    public const string Status = "status";
    public const string Summary = "summary";
    public const string Metrics = "metrics";
    public const string Model = "model";
    public const string Chains = "chains";
    public const string ChainsFactoryInfo = "chains/factory-info";
    public const string PerfSummary = "perf-summary";
    public const string Settings = "settings";
    public const string AutotunePresets = "autotune/presets";

    public const string MiningStart = "mining/start";
    public const string MiningStop = "mining/stop";
    public const string MiningRestart = "mining/restart";
    public const string MiningPause = "mining/pause";
    public const string MiningResume = "mining/resume";
    public const string MiningSwitch = "mining/switch-pool";

    public const string SystemReboot = "system/reboot";
}