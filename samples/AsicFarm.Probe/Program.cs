using System.CommandLine;
using AsicFarm.MinerClients.Vnish;
using AsicFarm.Shared.Contracts;
using AsicFarm.Shared.Domain;
using Microsoft.Extensions.Logging;

var hostArg = new Argument<string>("host", "Miner host or IP");
var pwOpt = new Option<string?>("--password", () => null, "Web UI password");
var ipOpt = new Option<bool>("--identity", "Print identity only");
var metricsOpt = new Option<bool>("--metrics", "Print metrics history");
var logOpt = new Option<string>("--log", () => "warning", "Log level");

var root = new RootCommand("VNISH Antminer probe")
{
    hostArg, pwOpt, ipOpt, metricsOpt, logOpt
};

root.SetHandler(async (string host, string? pw, bool identityOnly, bool metricsFlag, string logLevel) =>
{
    using var loggerFactory = LoggerFactory.Create(b =>
        b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(ParseLevel(logLevel)));
    var log = loggerFactory.CreateLogger<VnishAntminerClient>();

    await using var client = new VnishAntminerClient(
        new VnishAntminerClientOptions { Host = host, Password = pw },
        logger: log);

    Console.WriteLine($"-> Probing {host} ...");
    var probe = await client.ProbeAsync(CancellationToken.None);
    Console.WriteLine($"   reachable={probe.IsReachable} fw={probe.Firmware} ver={probe.FirmwareVersion} model={probe.Model}");
    if (!probe.IsReachable) return;

    var id = await client.GetIdentityAsync(CancellationToken.None);
    PrintIdentity(id);

    if (identityOnly) return;

    var snap = await client.GetSnapshotAsync(CancellationToken.None);
    PrintSnapshot(snap);

    if (metricsFlag)
    {
        var m = await client.GetMetricsAsync(TimeSpan.FromHours(1), TimeSpan.FromMinutes(5), CancellationToken.None);
        Console.WriteLine($"\nMetrics: {m.Points.Count} points, {m.Annotations.Count} annotations");
        foreach (var p in m.Points.TakeLast(5))
            Console.WriteLine($"  {p.Time:HH:mm:ss}  {p.HashrateGhs,10:F2} GH/s  chip_max={p.ChipMaxTempC}C  pcb_max={p.PcbMaxTempC}C  fan={p.FanDutyPercent}%  pwr={p.PowerConsumptionW}W");
    }
}, hostArg, pwOpt, ipOpt, metricsOpt, logOpt);

return await root.InvokeAsync(args);

static LogLevel ParseLevel(string s) => s.ToLowerInvariant() switch
{
    "trace" => LogLevel.Trace,
    "debug" => LogLevel.Debug,
    "info" or "information" => LogLevel.Information,
    "warn" or "warning" => LogLevel.Warning,
    "error" => LogLevel.Error,
    _ => LogLevel.Warning,
};

static void PrintIdentity(MinerIdentity id)
{
    Console.WriteLine("\n-- Identity -----------------------------");
    Console.WriteLine($"  Model        {id.Model} ({id.ModelCode})");
    Console.WriteLine($"  Serial       {id.Serial}");
    Console.WriteLine($"  MAC / IP     {id.Mac} / {id.Ip}");
    Console.WriteLine($"  Hostname     {id.Hostname}");
    Console.WriteLine($"  Algorithm    {id.Algorithm}");
    Console.WriteLine($"  Firmware     {id.FirmwareName} {id.FirmwareVersion}");
    Console.WriteLine($"  Platform     {id.Platform}  series={id.Series}  install={id.InstallType}");
    Console.WriteLine($"  Chains       {id.NumChains} x {id.ChipsPerChain} chips (domain {id.ChipsPerDomain})");
    if (id.OverclockLimits is { } oc)
        Console.WriteLine($"  OC limits    freq {oc.MinFreq}..{oc.MaxFreq} (def {oc.DefaultFreq}, warn {oc.WarnFreq})  volt {oc.MinVolt}..{oc.MaxVolt}");
}

static void PrintSnapshot(MinerSnapshot s)
{
    Console.WriteLine("\n-- Snapshot -----------------------------");
    Console.WriteLine($"  State        {s.State}  ({s.StateDuration})");
    Console.WriteLine($"  Hashrate     RT {s.HashrateRealtimeGhs:F2}  AVG {s.HashrateAverageGhs:F2}  NOM {s.HashrateNominalGhs:F2}  {s.HashratePercent:F1}%");
    Console.WriteLine($"  Power        {s.PowerConsumptionW} W  ({s.PowerEfficiencyJPerTh:F1} J/TH)");
    Console.WriteLine($"  Chip temp    {s.ChipTemp.Min}..{s.ChipTemp.Max} C");
    Console.WriteLine($"  PCB temp     {s.PcbTemp.Min}..{s.PcbTemp.Max} C");
    Console.WriteLine($"  PSU temp     llc1={s.PsuTempLlc1} llc2={s.PsuTempLlc2} pfc={s.PsuTempPfc}");
    Console.WriteLine($"  HW errors    {s.HwErrors} ({s.HwErrorsPercent:F3}%)  hr_error={s.HrError:F3}");
    Console.WriteLine($"  Devfee       {s.DevFeePercent:F2}%  found_blocks={s.FoundBlocks}  best_share={s.BestShare}");
    Console.WriteLine($"  Cooling      {s.Cooling.FanCount} fans  duty={s.Cooling.FanDutyPercent}%  mode={s.Cooling.Mode}");

    Console.WriteLine("\n  Chains:");
    foreach (var c in s.Chains)
        Console.WriteLine($"   #{c.Id}  {c.State,-12} {c.HashrateRealtimeGhs,8:F1}/{c.HashrateNominalGhs,-6:F1} GH/s  {c.HashratePercent,5:F1}%  freq={c.FrequencyMhz:F0}  volt={c.VoltageMv}  chip={c.ChipTemp.Min}..{c.ChipTemp.Max}C  pcb={c.PcbTemp.Min}..{c.PcbTemp.Max}C  errs={c.HwErrors}  chips[R{c.ChipStatuses.Red} O{c.ChipStatuses.Orange} G{c.ChipStatuses.Grey}]");

    Console.WriteLine("\n  Pools:");
    foreach (var p in s.Pools)
        Console.WriteLine($"   #{p.Id}  {p.Status,-10} {p.Kind,-9} {p.Url}  (u={p.User})  A={p.Accepted} R={p.Rejected} S={p.Stale}  ping={p.PingMs}ms");
}