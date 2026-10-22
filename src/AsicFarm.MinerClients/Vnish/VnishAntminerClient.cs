using System.Net;
using System.Text;
using System.Text.Json;
using AsicFarm.Shared.Contracts;
using AsicFarm.Shared.Domain;
using AsicFarm.Shared.Exceptions;
using AsicFarm.Shared.Vnish;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AsicFarm.MinerClients.Vnish;

public sealed class VnishAntminerClient : IAntminerClient
{
    private readonly VnishAntminerClientOptions _opt;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly ILogger<VnishAntminerClient> _log;
    private readonly VnishAuthState _auth;

    public string Host => _opt.Host;
    public FirmwareKind Firmware => FirmwareKind.Vnish;

    public VnishAntminerClient(VnishAntminerClientOptions options, HttpClient? httpClient = null, ILogger<VnishAntminerClient>? logger = null)
    {
        _opt = options ?? throw new ArgumentNullException(nameof(options));
        _log = logger ?? NullLogger<VnishAntminerClient>.Instance;
        _auth = new VnishAuthState(options.ApiKey);

        if (httpClient is null)
        {
            var handler = new SocketsHttpHandler { ConnectTimeout = options.ConnectTimeout, PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
            _http = new HttpClient(handler) { Timeout = options.RequestTimeout };
            _ownsHttp = true;
        }
        else { _http = httpClient; }
    }

    public async Task<FirmwareProbeResult> ProbeAsync(CancellationToken ct)
    {
        try
        {
            var info = await GetAsync<VnishInfo>(VnishEndpoints.Info, ct).ConfigureAwait(false);
            if (info is null) return new FirmwareProbeResult(false, FirmwareKind.Unknown, null, null, "empty /info");
            return new FirmwareProbeResult(true, FirmwareKind.Vnish, info.FirmwareVersion, info.Miner, null);
        }
        catch (MinerUnreachableException ex) { return new FirmwareProbeResult(false, FirmwareKind.Unknown, null, null, ex.Message); }
        catch (MinerApiException ex) { return new FirmwareProbeResult(true, FirmwareKind.Unknown, null, null, ex.Message); }
    }

    public async Task<MinerIdentity> GetIdentityAsync(CancellationToken ct)
    {
        var infoTask = GetAsync<VnishInfo>(VnishEndpoints.Info, ct);
        var modelTask = GetAsync<VnishMinerModelInfo>(VnishEndpoints.Model, ct);
        await Task.WhenAll(infoTask, modelTask).ConfigureAwait(false);

        var info = await infoTask.ConfigureAwait(false) ?? throw new MinerApiException(Host, 200, "empty /info");
        var model = await modelTask.ConfigureAwait(false);

        var minerId = !string.IsNullOrWhiteSpace(info.System.Network.Mac) ? info.System.Network.Mac : info.Serial;
        FanLimits? fanLimits = model?.Cooling.FanMinCount is { } fmc ? new FanLimits(fmc.Min, fmc.Max, fmc.Default) : null;
        OverclockLimits? oc = model?.Overclock is { } o ? new OverclockLimits(o.MinFreq, o.MaxFreq, o.DefaultFreq, o.WarnFreq, o.MinVoltage, o.MaxVoltage, o.DefaultVoltage, o.MaxVoltageStockPsu) : null;

        return new MinerIdentity(
            MinerId: minerId, Ip: info.System.Network.Ip, Mac: info.System.Network.Mac, Hostname: info.System.Network.Hostname,
            Serial: info.Serial, Model: info.Miner, ModelCode: info.Model, Algorithm: info.Algorithm, FirmwareName: info.FirmwareName,
            FirmwareVersion: info.FirmwareVersion, Platform: info.Platform, Series: model?.Series ?? "", InstallType: info.InstallType,
            ChipsPerChain: model?.Chain.ChipsPerChain, NumChains: model?.Chain.NumChains, ChipsPerDomain: model?.Chain.ChipsPerDomain,
            FanLimits: fanLimits, OverclockLimits: oc);
    }

    public Task<MinerSnapshot> GetSnapshotAsync(CancellationToken ct) => BuildSnapshotAsync(ct);
    public Task<MinerSnapshot> GetDetailedSnapshotAsync(CancellationToken ct) => BuildSnapshotAsync(ct);

    private async Task<MinerSnapshot> BuildSnapshotAsync(CancellationToken ct)
    {
        var infoTask = GetAsync<VnishInfo>(VnishEndpoints.Info, ct);
        var statusTask = GetAsync<VnishStatusPane>(VnishEndpoints.Status, ct);
        var sumTask = GetAsync<VnishSummary>(VnishEndpoints.Summary, ct);

        await Task.WhenAll(infoTask, statusTask, sumTask).ConfigureAwait(false);

        var info = await infoTask.ConfigureAwait(false) ?? throw new MinerApiException(Host, 200, "empty /info");
        var status = await statusTask.ConfigureAwait(false) ?? throw new MinerApiException(Host, 200, "empty /status");
        var summary = await sumTask.ConfigureAwait(false);

        if (summary?.Miner is not { } miner) return MinimalSnapshot(info, status);
        return ComposeSnapshot(info, status, miner);
    }

    private MinerSnapshot ComposeSnapshot(VnishInfo info, VnishStatusPane status, VnishAntmMinerStats miner)
    {
        var measure = info.HashrateMeasure;
        var hrRealtimeGhs = miner.HashrateRealtime.ToGhs(measure);
        var hrAverageGhs = miner.HashrateAverage.ToGhs(measure);
        var hrNominalGhs = miner.HashrateNominal.ToGhs(measure);
        var hrStockGhs = miner.HashrateStock.ToGhs(measure);
        var hrPercent = hrNominalGhs > 0 ? hrRealtimeGhs / hrNominalGhs * 100.0 : 0.0;
        var minerId = !string.IsNullOrWhiteSpace(info.System.Network.Mac) ? info.System.Network.Mac : info.Serial;

        return new MinerSnapshot(
            MinerId: minerId, IpAddress: info.System.Network.Ip, MacAddress: info.System.Network.Mac, Timestamp: DateTimeOffset.UtcNow, Firmware: FirmwareKind.Vnish,
            State: miner.MinerStatus.MinerState.ToMinerState(), StateDescription: miner.MinerStatus.Description ?? status.Description, FailureCode: miner.MinerStatus.FailureCode,
            StateDuration: miner.MinerStatus.MinerStateTime > 0 ? TimeSpan.FromSeconds(miner.MinerStatus.MinerStateTime) : null,
            HashrateRealtimeGhs: hrRealtimeGhs, HashrateAverageGhs: hrAverageGhs, HashrateNominalGhs: hrNominalGhs, HashrateStockGhs: hrStockGhs, HashratePercent: hrPercent,
            PowerConsumptionW: miner.PowerConsumption, PowerEfficiencyJPerTh: miner.PowerEfficiency,
            ChipTemp: miner.ChipTemp.ToTempRange(), PcbTemp: miner.PcbTemp.ToTempRange(), PsuTempLlc1: miner.Psu?.Temps?.Llc1, PsuTempLlc2: miner.Psu?.Temps?.Llc2, PsuTempPfc: miner.Psu?.Temps?.Pfc,
            HwErrors: miner.HwErrors, HwErrorsPercent: miner.HwErrorsPercent, HrError: miner.HrError, FoundBlocks: miner.FoundBlocks, BestShare: miner.BestShare, DevFeePercent: miner.DevFeePercent,
            Chains: miner.Chains.Select(c => c.ToChainSnapshot()).ToList(), Pools: miner.Pools.Select(p => p.ToPoolSnapshot()).ToList(), Cooling: miner.Cooling.ToCoolingSnapshot(),
            Model: info.Miner, ModelCode: info.Model, Algorithm: info.Algorithm, FirmwareVersion: info.FirmwareVersion, FirmwareName: info.FirmwareName, SerialNumber: info.Serial, Hostname: info.System.Network.Hostname,
            ChipsPerChain: null, NumChains: miner.Chains.Count > 0 ? miner.Chains.Count : null);
    }

    private MinerSnapshot MinimalSnapshot(VnishInfo info, VnishStatusPane status)
    {
        var minerId = !string.IsNullOrWhiteSpace(info.System.Network.Mac) ? info.System.Network.Mac : info.Serial;
        return new MinerSnapshot(
            MinerId: minerId, IpAddress: info.System.Network.Ip, MacAddress: info.System.Network.Mac, Timestamp: DateTimeOffset.UtcNow, Firmware: FirmwareKind.Vnish,
            State: status.MinerState.ToMinerState(), StateDescription: status.Description, FailureCode: status.FailureCode, StateDuration: status.MinerStateTime > 0 ? TimeSpan.FromSeconds(status.MinerStateTime) : null,
            HashrateRealtimeGhs: 0, HashrateAverageGhs: 0, HashrateNominalGhs: 0, HashrateStockGhs: 0, HashratePercent: 0, PowerConsumptionW: 0, PowerEfficiencyJPerTh: 0,
            ChipTemp: TempRange.Empty, PcbTemp: TempRange.Empty, PsuTempLlc1: null, PsuTempLlc2: null, PsuTempPfc: null, HwErrors: 0, HwErrorsPercent: 0, HrError: 0, FoundBlocks: 0, BestShare: 0, DevFeePercent: 0,
            Chains: Array.Empty<ChainSnapshot>(), Pools: Array.Empty<PoolSnapshot>(),
            Cooling: new CoolingSnapshot(0, 0, FanControlMode.Auto, Array.Empty<FanSnapshot>()), Model: info.Miner, ModelCode: info.Model, Algorithm: info.Algorithm, FirmwareVersion: info.FirmwareVersion, FirmwareName: info.FirmwareName, SerialNumber: info.Serial, Hostname: info.System.Network.Hostname,
            ChipsPerChain: null, NumChains: null);
    }

    public async Task<MinerDetailedSnapshot> GetFullDetailsAsync(CancellationToken ct)
    {
        var infoTask = GetAsync<VnishInfo>(VnishEndpoints.Info, ct);
        var statusTask = GetAsync<VnishStatusPane>(VnishEndpoints.Status, ct);
        var sumTask = GetAsync<VnishSummary>(VnishEndpoints.Summary, ct);
        var chainsTask = GetAsyncList<VnishAntmChainChips>(VnishEndpoints.Chains, ct);
        var modelTask = GetAsync<VnishMinerModelInfo>(VnishEndpoints.Model, ct);
        var factoryTask = GetAsync<VnishFactoryInfoReply>(VnishEndpoints.ChainsFactoryInfo, ct);

        await Task.WhenAll(infoTask, statusTask, sumTask, chainsTask, modelTask, factoryTask).ConfigureAwait(false);

        var info = await infoTask.ConfigureAwait(false) ?? throw new MinerApiException(Host, 200, "empty /info");
        var status = await statusTask.ConfigureAwait(false) ?? throw new MinerApiException(Host, 200, "empty /status");
        var summary = await sumTask.ConfigureAwait(false);
        var chains = await chainsTask.ConfigureAwait(false) ?? new List<VnishAntmChainChips>();
        var model = await modelTask.ConfigureAwait(false);
        var factory = await factoryTask.ConfigureAwait(false);

        var summarySnapshot = summary?.Miner is { } miner ? ComposeSnapshot(info, status, miner) : MinimalSnapshot(info, status);

        var chainDetails = new List<ChainChipsInfo>(chains.Count);
        foreach (var c in chains)
        {
            var chips = c.Chips.Select(ch => new ChipInfo(ch.Id, c.Id, ch.Hashrate, ch.Frequency, ch.Voltage, ch.Temp, ch.Errors, ch.Grade.ToChipGrade(), ch.Throttled)).ToList();
            var sensors = c.Sensors.Select(s => new ChipSensorInfo(s.Board, s.Chip, s.Location, s.State)).ToList();
            chainDetails.Add(new ChainChipsInfo(c.Id, c.Status.State.ToChainState(), c.Status.FailureMessage, c.HashrateRealtime, c.HashrateNominal, c.Frequency, chips, sensors));
        }

        var topologies = new List<ChainTopology>();
        if (model?.Chain.Topology is { } topo && chains.Count > 0)
            foreach (var c in chains) topologies.Add(new ChainTopology(c.Id, topo.NumRows, topo.NumCols, topo.Chips));

        FactoryInfo? factoryInfo = null;
        if (factory is not null)
        {
            var fchains = factory.Chains?.Select(fc => new FactoryChainInfo(fc.Id, fc.BoardModel, fc.Serial, fc.ChipBin, fc.Frequency, fc.Voltage, fc.Hashrate)).ToList() ?? new List<FactoryChainInfo>();
            factoryInfo = new FactoryInfo(fchains, factory.HashrateStock, factory.PsuModel, factory.PsuSerial, factory.HasPics);
        }

        return new MinerDetailedSnapshot(summarySnapshot.MinerId, DateTimeOffset.UtcNow, summarySnapshot, chainDetails, topologies, factoryInfo);
    }

    public async Task<MetricsHistory> GetMetricsAsync(TimeSpan window, TimeSpan step, CancellationToken ct)
    {
        var sliceSeconds = (int)Math.Clamp(window.TotalSeconds, 60, 3 * 24 * 60 * 60);
        var stepSeconds = (int)Math.Clamp(step.TotalSeconds, 10, 24 * 60 * 60);
        var url = $"{VnishEndpoints.Metrics}?time_slice={sliceSeconds}&step={stepSeconds}";
        var reply = await GetAsync<VnishMetricsReply>(url, ct).ConfigureAwait(false) ?? throw new MinerApiException(Host, 200, "empty /metrics");

        var points = reply.Metrics.Select(m => new MetricsPoint(DateTimeOffset.FromUnixTimeSeconds(m.Time), m.Data.Hashrate, m.Data.PcbMaxTemp, m.Data.ChipMaxTemp, m.Data.FanDuty, m.Data.PowerConsumption)).ToList();
        var annotations = reply.Annotations.Select(a => new MinerEventAnnotation(DateTimeOffset.FromUnixTimeSeconds(a.Time), a.Data.Type.ToEventKind(), a.Data.ChainId)).ToList();
        return new MetricsHistory(points, annotations);
    }

    public Task RebootAsync(CancellationToken ct) => PostAsync(VnishEndpoints.SystemReboot, ct);
    public Task StartMiningAsync(CancellationToken ct) => PostAsync(VnishEndpoints.MiningStart, ct);
    public Task StopMiningAsync(CancellationToken ct) => PostAsync(VnishEndpoints.MiningStop, ct);
    public Task RestartMiningAsync(CancellationToken ct) => PostAsync(VnishEndpoints.MiningRestart, ct);
    public Task PauseMiningAsync(CancellationToken ct) => PostAsync(VnishEndpoints.MiningPause, ct);
    public Task ResumeMiningAsync(CancellationToken ct) => PostAsync(VnishEndpoints.MiningResume, ct);
    public Task SwitchPoolAsync(int poolId, CancellationToken ct) => PostJsonAsync(VnishEndpoints.MiningSwitch, new VnishSwitchPoolBody(poolId), ct);

    public async Task SetPoolsAsync(List<PoolConfigDto> pools, CancellationToken ct)
    {
        _log.LogInformation("Vnish {Host}: SetPools called with {Count} pools", Host, pools.Count);
        var safePools = new List<PoolConfigDto>(pools);
        while (safePools.Count < 3) safePools.Add(new PoolConfigDto("", "", ""));

        var payload = new
        {
            miner = new
            {
                pools = new[]
                {
                    new { url = safePools[0].Url, user = safePools[0].Worker, pass = safePools[0].Password },
                    new { url = safePools[1].Url, user = safePools[1].Worker, pass = safePools[1].Password },
                    new { url = safePools[2].Url, user = safePools[2].Worker, pass = safePools[2].Password }
                }
            }
        };

        _log.LogInformation("Vnish {Host}: POST {Path}, body: {Body}", Host, VnishEndpoints.Settings, JsonSerializer.Serialize(payload, VnishJson.Options));

        VnishSaveConfigResult? result;
        try { result = await PostJsonAsync<object, VnishSaveConfigResult>(VnishEndpoints.Settings, payload, ct); }
        catch (MinerApiException ex) { _log.LogError("Vnish {Host}: settings API returned error: {Msg}", Host, ex.Message); throw; }

        _log.LogInformation("Vnish {Host}: settings saved. RestartRequired={Restart}, RebootRequired={Reboot}", Host, result?.RestartRequired, result?.RebootRequired);

        if (result?.RebootRequired == true) { _log.LogInformation("Vnish {Host}: reboot required, calling /system/reboot", Host); await RebootAsync(ct); }
        else if (result?.RestartRequired == true) { _log.LogInformation("Vnish {Host}: restart required, calling /mining/restart", Host); await RestartMiningAsync(ct); }
        else { _log.LogInformation("Vnish {Host}: no restart required (result={Result})", Host, result is null ? "NULL - likely 200 with empty body" : "OK"); }
    }

    public async Task SetTargetTemperatureAsync(int targetTemp, CancellationToken ct)
    {
        _log.LogInformation("Vnish {Host}: SetTargetTemperature({Temp}°C)", Host, targetTemp);

        // VNISH хранит целевую температуру внутри mode.param
        var payload = new
        {
            miner = new
            {
                cooling = new
                {
                    mode = new
                    {
                        name = "auto",
                        param = targetTemp
                    }
                }
            }
        };

        var jsonBody = JsonSerializer.Serialize(payload, VnishJson.Options);
        _log.LogInformation("Vnish {Host}: POST /settings body: {Body}", Host, jsonBody);

        try
        {
            var result = await PostJsonAsync<object, VnishSaveConfigResult>(VnishEndpoints.Settings, payload, ct);
            _log.LogInformation("Vnish {Host}: target_temp response: RestartRequired={Restart}", Host, result?.RestartRequired);

            if (result?.RestartRequired == true)
            {
                _log.LogInformation("Vnish {Host}: restart required after target_temp change", Host);
                await RestartMiningAsync(ct);
            }
        }
        catch (MinerApiException ex)
        {
            _log.LogError("Vnish {Host}: target_temp API error: {Msg}", Host, ex.Message);
            throw;
        }
    }

    public async Task ConfigurePresetSwitcherAsync(bool enabled, string minPreset, string maxPreset, int riseTemp, int decreaseTemp, CancellationToken ct)
    {
        _log.LogInformation("Vnish {Host}: ConfigurePresetSwitcher(enabled={En}, min={Min}, max={Max}, rise={Rise}, dec={Dec})", Host, enabled, minPreset, maxPreset, riseTemp, decreaseTemp);
        var payload = new { miner = new { overclock = new { preset_switcher = new { enabled = enabled, min_preset = minPreset, top_preset = maxPreset, rise_temp = riseTemp, decrease_temp = decreaseTemp } } } };

        VnishSaveConfigResult? result;
        try
        {
            result = await PostJsonAsync<object, VnishSaveConfigResult>(VnishEndpoints.Settings, payload, ct);
        }
        catch (MinerApiException ex)
        {
            _log.LogError("Vnish {Host}: preset_switcher API error: {Msg}", Host, ex.Message);
            throw;
        }

        _log.LogInformation("Vnish {Host}: preset_switcher saved. RestartRequired={Restart}", Host, result?.RestartRequired);

        if (result?.RestartRequired == true)
        {
            _log.LogInformation("Vnish {Host}: restart required after preset_switcher change", Host);
            await RestartMiningAsync(ct);
        }
    }

    public async Task<List<VnishAutotunePreset>> GetAutotunePresetsAsync(CancellationToken ct)
    {
        _log.LogInformation("Vnish {Host}: GetAutotunePresetsAsync START", Host);
        try
        {
            var presets = await GetAsyncList<VnishAutotunePreset>(VnishEndpoints.AutotunePresets, ct).ConfigureAwait(false);
            _log.LogInformation("Vnish {Host}: GetAutotunePresetsAsync returned {Count} presets", Host, presets?.Count ?? 0);
            return presets ?? new List<VnishAutotunePreset>();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Vnish {Host}: GetAutotunePresetsAsync FAILED", Host);
            throw;
        }
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct) { using var req = new HttpRequestMessage(HttpMethod.Get, BuildUri(path)); return await SendAsync<T>(req, ct).ConfigureAwait(false); }
    private async Task<List<T>?> GetAsyncList<T>(string path, CancellationToken ct) { using var req = new HttpRequestMessage(HttpMethod.Get, BuildUri(path)); return await SendAsync<List<T>>(req, ct).ConfigureAwait(false); }
    private async Task PostAsync(string path, CancellationToken ct) { using var req = new HttpRequestMessage(HttpMethod.Post, BuildUri(path)); await SendAsync<object>(req, ct).ConfigureAwait(false); }
    private Task PostJsonAsync<TBody>(string path, TBody body, CancellationToken ct) => PostJsonAsync<TBody, object>(path, body, ct);
    private async Task<T?> PostJsonAsync<TBody, T>(string path, TBody body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, BuildUri(path)) { Content = new StringContent(JsonSerializer.Serialize(body, VnishJson.Options), Encoding.UTF8, "application/json") };
        return await SendAsync<T>(req, ct).ConfigureAwait(false);
    }

    private Uri BuildUri(string path) => new(_opt.BaseAddress, path);

    private async Task<T?> SendAsync<T>(HttpRequestMessage req, CancellationToken ct)
    {
        await AttachAuthAsync(req, ct).ConfigureAwait(false);
        HttpResponseMessage response;
        try { response = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false); }
        catch (HttpRequestException ex) { throw new MinerUnreachableException(Host, ex); }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) { throw new MinerUnreachableException(Host, ex); }

        if (response.StatusCode == HttpStatusCode.Unauthorized && !_auth.HasStaticApiKey && _opt.Password is { Length: > 0 })
        {
            response.Dispose(); _auth.Invalidate();
            _log.LogDebug("Vnish {Host}: 401, re-authenticating", Host);
            using var retryReq = await CloneAsync(req, ct).ConfigureAwait(false);
            await AttachAuthAsync(retryReq, ct).ConfigureAwait(false);
            try { response = await _http.SendAsync(retryReq, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false); }
            catch (HttpRequestException ex) { throw new MinerUnreachableException(Host, ex); }
        }

        using (response) { return await ReadResponseAsync<T>(response, ct).ConfigureAwait(false); }
    }

    private async Task<T?> ReadResponseAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await SafeReadAsync(response, ct).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new MinerAuthException(Host, $"HTTP {(int)response.StatusCode}: {body}");
            throw new MinerApiException(Host, (int)response.StatusCode, body);
        }

        if (response.Content.Headers.ContentLength is 0) return default;
        if (typeof(T) == typeof(object)) return default;

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        try { return await JsonSerializer.DeserializeAsync<T>(stream, VnishJson.Options, ct).ConfigureAwait(false); }
        catch (JsonException ex) { var body = await SafeReadAsync(response, ct).ConfigureAwait(false); throw new MinerApiException(Host, (int)response.StatusCode, $"Failed to deserialize as {typeof(T).Name}: {ex.Message}. Body: {body}"); }
    }

    private async Task AttachAuthAsync(HttpRequestMessage req, CancellationToken ct)
    {
        if (_auth.HasStaticApiKey) { req.Headers.TryAddWithoutValidation("x-api-key", _auth.ApiKey); return; }
        if (_opt.Password is { Length: > 0 })
        {
            var token = await _auth.GetOrAcquireTokenAsync(AcquireTokenAsync, ct).ConfigureAwait(false);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
    }

    private async Task<string> AcquireTokenAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, BuildUri(VnishEndpoints.Unlock)) { Content = new StringContent(JsonSerializer.Serialize(new VnishUnlockBody(_opt.Password!), VnishJson.Options), Encoding.UTF8, "application/json") };
        HttpResponseMessage resp;
        try { resp = await _http.SendAsync(req, ct).ConfigureAwait(false); }
        catch (HttpRequestException ex) { throw new MinerUnreachableException(Host, ex); }

        using (resp)
        {
            var body = await SafeReadAsync(resp, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) throw new MinerAuthException(Host, $"unlock failed: HTTP {(int)resp.StatusCode} {body}");
            if (body is null) throw new MinerAuthException(Host, "unlock returned empty body");
            var parsed = JsonSerializer.Deserialize<VnishUnlockSuccess>(body, VnishJson.Options);
            if (parsed?.Token is not { Length: > 0 }) throw new MinerAuthException(Host, "unlock returned empty token");
            _log.LogDebug("Vnish {Host}: obtained JWT token", Host);
            return parsed.Token;
        }
    }

    private static async Task<string?> SafeReadAsync(HttpResponseMessage resp, CancellationToken ct) { try { return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false); } catch { return null; } }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage req, CancellationToken ct)
    {
        var clone = new HttpRequestMessage(req.Method, req.RequestUri);
        if (req.Content is not null)
        {
            var bytes = await req.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var h in req.Content.Headers) clone.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }
        foreach (var h in req.Headers) clone.Headers.TryAddWithoutValidation(h.Key, h.Value);
        return clone;
    }

    public ValueTask DisposeAsync() { if (_ownsHttp) _http.Dispose(); return ValueTask.CompletedTask; }
}