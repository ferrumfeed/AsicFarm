using AsicFarm.Shared.Contracts;
using AsicFarm.Shared.Domain;
using AsicFarm.Shared.Vnish;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace AsicFarm.MinerClients.Whatsminer;

public sealed class WhatsminerAntminerClient : IAntminerClient
{
    private readonly WhatsminerTcpTransport _transport;
    private readonly WhatsminerTokenManager? _tokenManager;
    private readonly ILogger<WhatsminerAntminerClient> _log;
    private readonly string _host;
    private readonly TimeSpan _timeout;

    public string Host => _host;
    public FirmwareKind Firmware => FirmwareKind.Whatsminer;

    public WhatsminerAntminerClient(string host, string? adminPassword = null, TimeSpan? timeout = null, ILogger<WhatsminerAntminerClient>? logger = null)
    {
        _host = host;
        _timeout = timeout ?? TimeSpan.FromSeconds(20);
        _log = logger ?? NullLogger<WhatsminerAntminerClient>.Instance;
        _transport = new WhatsminerTcpTransport(host);
        if (!string.IsNullOrEmpty(adminPassword)) _tokenManager = new WhatsminerTokenManager(adminPassword);
    }

    public async Task<FirmwareProbeResult> ProbeAsync(CancellationToken ct)
    {
        try
        {
            await _transport.ConnectAsync(_timeout, ct);
            using var doc = await _transport.SendCommandAsync(new { cmd = "get.device.info" }, _timeout, ct);
            var root = doc.RootElement;
            if (root.GetProperty("code").GetInt32() != 0) return new FirmwareProbeResult(true, FirmwareKind.Unknown, null, null, "non-zero code");
            var msg = root.GetProperty("msg");
            var miner = msg.GetProperty("miner");
            var system = msg.GetProperty("system");
            return new FirmwareProbeResult(true, FirmwareKind.Whatsminer, TryGetString(system, "fwversion"), TryGetString(miner, "type"), null);
        }
        catch (Exception ex) { return new FirmwareProbeResult(false, FirmwareKind.Unknown, null, null, ex.Message); }
    }

    public async Task<MinerIdentity> GetIdentityAsync(CancellationToken ct)
    {
        await EnsureConnectedAsync(ct);
        var devInfo = await SendReadAsync("get.device.info", null, ct);
        var network = devInfo.GetProperty("network");
        var miner = devInfo.GetProperty("miner");
        var system = devInfo.GetProperty("system");
        var power = devInfo.GetProperty("power");
        var mac = TryGetString(network, "mac") ?? "";

        return new MinerIdentity(
            MinerId: string.IsNullOrEmpty(mac) ? _host : mac, Ip: TryGetString(network, "ip") ?? _host, Mac: mac,
            Hostname: TryGetString(network, "hostname") ?? "", Serial: TryGetString(power, "sn") ?? "",
            Model: TryGetString(miner, "type") ?? "", ModelCode: TryGetString(miner, "type") ?? "", Algorithm: "SHA256",
            FirmwareName: "Whatsminer", FirmwareVersion: TryGetString(system, "fwversion") ?? "", Platform: TryGetString(system, "platform") ?? "Whatsminer",
            Series: "", InstallType: "", ChipsPerChain: null, NumChains: TryGetInt(miner, "board-num"), ChipsPerDomain: null, FanLimits: null, OverclockLimits: null);
    }

    public async Task<MinerSnapshot> GetSnapshotAsync(CancellationToken ct)
    {
        await EnsureConnectedAsync(ct);
        var devInfo = await SendReadAsync("get.device.info", null, ct);
        var statusMsg = await SendReadAsync("get.miner.status", "summary", ct);
        if (!statusMsg.TryGetProperty("summary", out var summary)) throw new InvalidOperationException($"Whatsminer {_host}: get.miner.status returned no summary");
        return MapToSnapshot(devInfo, summary);
    }

    public Task<MinerSnapshot> GetDetailedSnapshotAsync(CancellationToken ct) => GetSnapshotAsync(ct);

    public async Task<MinerDetailedSnapshot> GetFullDetailsAsync(CancellationToken ct)
    {
        await EnsureConnectedAsync(ct);
        var devInfo = await SendReadAsync("get.device.info", null, ct);
        var summaryMsg = await SendReadAsync("get.miner.status", "summary", ct);
        var edevsMsg = await SendReadAsync("get.miner.status", "edevs", ct);
        var poolsMsg = await SendReadAsync("get.miner.status", "pools", ct);
        var summary = summaryMsg.TryGetProperty("summary", out var s) ? s : throw new InvalidOperationException($"Whatsminer {_host}: no summary");
        var snapshot = MapToSnapshot(devInfo, summary);

        var chains = new List<ChainChipsInfo>();
        if (edevsMsg.TryGetProperty("edevs", out var edevs) && edevs.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in edevs.EnumerateArray())
            {
                var id = TryGetInt(e, "id") ?? 0;
                var hashAvgTh = TryGetDouble(e, "hash-average") ?? 0;
                var factoryHashTh = TryGetDouble(e, "factory-hash") ?? 0;
                var freq = TryGetInt(e, "freq") ?? 0;
                var effectiveChips = TryGetInt(e, "effective-chips") ?? 0;
                var state = effectiveChips > 0 ? ChainState.Mining : ChainState.Failure;
                chains.Add(new ChainChipsInfo(id, state, null, hashAvgTh * 1000.0, factoryHashTh * 1000.0, freq, new List<ChipInfo>(), new List<ChipSensorInfo>()));
            }
        }

        var pools = new List<PoolSnapshot>();
        if (poolsMsg.TryGetProperty("pools", out var poolsArr) && poolsArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in poolsArr.EnumerateArray())
            {
                var pid = TryGetInt(p, "id") ?? 0;
                var url = TryGetString(p, "url") ?? "";
                var account = TryGetString(p, "account") ?? "";
                var active = p.TryGetProperty("stratum-active", out var sa) && sa.ValueKind == JsonValueKind.True;
                var state = active ? PoolState.Active : PoolState.Offline;
                string lastShareTime = "";
                var lastShareRaw = TryGetDouble(p, "last-share-time");
                if (lastShareRaw is { } lst && lst < int.MaxValue - 1) lastShareTime = DateTimeOffset.FromUnixTimeSeconds((long)lst).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                pools.Add(new PoolSnapshot(pid, url, account, PoolKind.UserPool, state, false, "", 0.0, 0, 0, 0, 0, 0.0, lastShareTime));
            }
        }

        return new MinerDetailedSnapshot(snapshot.MinerId, DateTimeOffset.UtcNow, snapshot, chains, new List<ChainTopology>(), null);
    }

    public Task<MetricsHistory> GetMetricsAsync(TimeSpan window, TimeSpan step, CancellationToken ct)
        => Task.FromResult(new MetricsHistory(new List<MetricsPoint>(), new List<MinerEventAnnotation>()));

    public Task RebootAsync(CancellationToken ct) => SendWriteAsync("set.miner.reboot", new { }, ct);
    public Task StartMiningAsync(CancellationToken ct) => SendWriteAsync("set.miner.resume", new { }, ct);
    public Task StopMiningAsync(CancellationToken ct) => SendWriteAsync("set.miner.pause", new { }, ct);
    public Task RestartMiningAsync(CancellationToken ct) => SendWriteAsync("set.miner.restart", new { }, ct);
    public Task PauseMiningAsync(CancellationToken ct) => SendWriteAsync("set.miner.pause", new { }, ct);
    public Task ResumeMiningAsync(CancellationToken ct) => SendWriteAsync("set.miner.resume", new { }, ct);
    public Task SwitchPoolAsync(int poolId, CancellationToken ct) => throw new NotSupportedException("Whatsminer does not support switching active pool directly; use SetPoolsAsync");

    public async Task SetPoolsAsync(List<PoolConfigDto> pools, CancellationToken ct)
    {
        var safePools = new List<PoolConfigDto>(pools);
        while (safePools.Count < 3) safePools.Add(new PoolConfigDto("", "", ""));
        var payload = new { pools = new[] { new { url = safePools[0].Url, user = safePools[0].Worker, pass = safePools[0].Password }, new { url = safePools[1].Url, user = safePools[1].Worker, pass = safePools[1].Password }, new { url = safePools[2].Url, user = safePools[2].Worker, pass = safePools[2].Password } } };
        await SendWriteAsync("set.miner.pools", payload, ct);
    }

    public Task SetTargetTemperatureAsync(int targetTemp, CancellationToken ct) => throw new NotSupportedException("Whatsminer does not support target temperature directly; use SetFanTempOffsetAsync");
    public Task SetCoolingModeAsync(string mode, int fanDuty, CancellationToken ct) => throw new NotSupportedException("Whatsminer fan control does not support mode/fan duty; use SetFanTempOffsetAsync");
    public Task SetQuietModeAsync(bool enabled, CancellationToken ct) => throw new NotSupportedException("Whatsminer does not support quiet mode");
    public Task SetRestartTempAsync(int temp, CancellationToken ct) => throw new NotSupportedException("Whatsminer does not support restart temperature");
    public Task SetStartupDelayAsync(int seconds, CancellationToken ct) => throw new NotSupportedException("Whatsminer does not support startup delay");
    public Task ConfigurePresetSwitcherAsync(bool enabled, string minPreset, string maxPreset, int riseTemp, int decreaseTemp, CancellationToken ct) => throw new NotSupportedException("Preset switcher is VNISH-specific");
    public Task<List<VnishAutotunePreset>> GetAutotunePresetsAsync(CancellationToken ct) => throw new NotSupportedException("Autotune presets are VNISH-specific");

    public Task<MinerSettingsDto> GetSettingsAsync(CancellationToken ct)
        => Task.FromResult(new MinerSettingsDto(65, "auto", 50, false, 90, 0, false, null, null, 80, 70));

    public Task<string> GetRawSettingsJsonAsync(CancellationToken ct)
        => Task.FromResult("{}");

    public Task SetPowerLimitAsync(int watts, CancellationToken ct) => SendWriteAsync("set.miner.power_limit", new { power_limit = watts }, ct);
    public Task SetFanTempOffsetAsync(int offsetC, CancellationToken ct) => SendWriteAsync("set.fan.temp_offset", new { temp_offset = offsetC }, ct);
    public Task SetFanZeroSpeedAsync(bool enabled, CancellationToken ct) => SendWriteAsync("set.fan.zero_speed", new { fan_zero_speed = enabled ? 1 : 0 }, ct);

    private MinerSnapshot MapToSnapshot(JsonElement devInfo, JsonElement summary)
    {
        var network = devInfo.GetProperty("network"); var miner = devInfo.GetProperty("miner"); var system = devInfo.GetProperty("system"); var power = devInfo.GetProperty("power");
        var mac = TryGetString(network, "mac") ?? ""; var ip = TryGetString(network, "ip") ?? _host; var hostname = TryGetString(network, "hostname") ?? "";
        var model = TryGetString(miner, "type") ?? ""; var fwVersion = TryGetString(system, "fwversion") ?? ""; var serial = TryGetString(power, "sn") ?? "";
        var hashRealtimeTh = TryGetDouble(summary, "hash-realtime") ?? 0; var hash15minTh = TryGetDouble(summary, "hash-15min") ?? 0;
        var hashAverageTh = TryGetDouble(summary, "hash-average") ?? 0; var factoryHashTh = TryGetDouble(summary, "factory-hash") ?? 0;
        var hashrateRealtimeGhs = hashRealtimeTh * 1000.0; var hashrateAverageGhs = (hash15minTh > 0 ? hash15minTh : hashAverageTh) * 1000.0;
        var hashrateNominalGhs = factoryHashTh * 1000.0; var hashratePercent = hashrateNominalGhs > 0 ? hashrateRealtimeGhs / hashrateNominalGhs * 100.0 : 0;
        var powerW = (int)(TryGetDouble(summary, "power-realtime") ?? 0); var fanInRpm = (int)(TryGetDouble(summary, "fan-speed-in") ?? 0); var fanOutRpm = (int)(TryGetDouble(summary, "fan-speed-out") ?? 0);
        var chipMin = (int)(TryGetDouble(summary, "chip-temp-min") ?? 0); var chipMax = (int)(TryGetDouble(summary, "chip-temp-max") ?? 0);
        var chipTemp = chipMax > 0 ? new TempRange(chipMin, chipMax) : TempRange.Empty; var (pcbMin, pcbMax) = GetBoardTempRange(summary);
        var psuTemp0 = (int)(TryGetDouble(power, "temp0") ?? 0); var elapsedSec = (long)(TryGetDouble(summary, "elapsed") ?? 0); var stateDuration = elapsedSec > 0 ? TimeSpan.FromSeconds(elapsedSec) : (TimeSpan?)null;
        int? failureCode = null;
        if (devInfo.TryGetProperty("error-code", out var errs) && errs.ValueKind == JsonValueKind.Array) { foreach (var err in errs.EnumerateArray()) { if (err.ValueKind != JsonValueKind.Object) continue; foreach (var prop in err.EnumerateObject()) { if (int.TryParse(prop.Name, out var code)) { failureCode = code; break; } } if (failureCode != null) break; } }
        var hasAnyHashrate = hashrateRealtimeGhs > 0; var state = hasAnyHashrate ? MinerState.Mining : failureCode != null ? MinerState.Failure : MinerState.Stopped;
        var stateDesc = hasAnyHashrate ? (failureCode != null ? $"warning {failureCode}" : null) : failureCode != null ? $"error {failureCode}" : "stopped";
        var fans = new List<FanSnapshot>(2); if (fanInRpm > 0) fans.Add(new FanSnapshot(0, fanInRpm, 0, FanState.Ok)); if (fanOutRpm > 0) fans.Add(new FanSnapshot(1, fanOutRpm, 0, FanState.Ok));
        var efficiencyJPerTh = hashrateRealtimeGhs > 0 ? powerW / (hashrateRealtimeGhs / 1000.0) : 0;
        return new MinerSnapshot(string.IsNullOrEmpty(mac) ? _host : mac, ip, mac, DateTimeOffset.UtcNow, FirmwareKind.Whatsminer, state, stateDesc, failureCode, stateDuration, hashrateRealtimeGhs, hashrateAverageGhs, hashrateNominalGhs, 0, hashratePercent, powerW, efficiencyJPerTh, chipTemp, pcbMax > 0 ? new TempRange(pcbMin, pcbMax) : TempRange.Empty, psuTemp0 > 0 ? psuTemp0 : null, null, null, 0, 0, 0, 0, 0, 0, Array.Empty<ChainSnapshot>(), Array.Empty<PoolSnapshot>(), new CoolingSnapshot(fans.Count, 0, FanControlMode.Auto, fans.ToArray()), model, model, "SHA256", fwVersion, "Whatsminer", serial, hostname, null, TryGetInt(miner, "board-num"));
    }

    private static (int min, int max) GetBoardTempRange(JsonElement summary)
    {
        if (!summary.TryGetProperty("board-temperature", out var arr) || arr.ValueKind != JsonValueKind.Array) return (0, 0);
        double? min = null, max = null; foreach (var item in arr.EnumerateArray()) { if (item.ValueKind != JsonValueKind.Number) continue; var v = item.GetDouble(); if (v <= 0) continue; if (min is null || v < min) min = v; if (max is null || v > max) max = v; }
        return ((int)(min ?? 0), (int)(max ?? 0));
    }

    private async Task EnsureConnectedAsync(CancellationToken ct) => await _transport.ConnectAsync(_timeout, ct);

    private async Task<JsonElement> SendReadAsync(string cmd, string? param, CancellationToken ct)
    {
        object payload = param is null ? new { cmd } : new { cmd, param };
        using var doc = await _transport.SendCommandAsync(payload, _timeout, ct);
        var root = doc.RootElement; if (root.GetProperty("code").GetInt32() != 0) throw new InvalidOperationException($"Whatsminer {_host}: '{cmd}' failed with code {root.GetProperty("code").GetInt32()}");
        return root.GetProperty("msg").Clone();
    }

    private async Task SendWriteAsync(string cmd, object param, CancellationToken ct)
    {
        if (_tokenManager is null) throw new InvalidOperationException($"Whatsminer {_host}: admin password not provided");
        await EnsureConnectedAsync(ct); var token = await _tokenManager.GetTokenAsync(_transport, ct);
        var payload = new { cmd, ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), token, account = "super", param };
        using var doc = await _transport.SendCommandAsync(payload, _timeout, ct); var code = doc.RootElement.GetProperty("code").GetInt32();
        if (code != 0) throw new InvalidOperationException($"Whatsminer {_host}: '{cmd}' failed with code {code}");
    }

    private static string? TryGetString(JsonElement element, string name) { if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var p)) return null; return p.ValueKind == JsonValueKind.String ? p.GetString() : p.ToString(); }
    private static double? TryGetDouble(JsonElement element, string name) { if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var p)) return null; if (p.ValueKind == JsonValueKind.Number) return p.GetDouble(); if (p.ValueKind == JsonValueKind.String && double.TryParse(p.GetString(), out var d)) return d; return null; }
    private static int? TryGetInt(JsonElement element, string name) { if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var p)) return null; if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var i)) return i; if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out i)) return i; return null; }

    public ValueTask DisposeAsync() => _transport.DisposeAsync();
}