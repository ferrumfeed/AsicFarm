using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using AsicFarm.Shared.Contracts;
using AsicFarm.Shared.Domain;
using AsicFarm.Shared.Vnish;

namespace AsicFarm.Client.Api;

public sealed class ServerApiClient
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private string _baseUrl = "";
    private string? _apiKey;

    public ServerApiClient(HttpClient http) => _http = http;

    public void Configure(string baseUrl, string apiKey)
    {
        _baseUrl = baseUrl.TrimEnd('/') + "/";
        _apiKey = apiKey;
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_baseUrl) && !string.IsNullOrEmpty(_apiKey);

    private HttpRequestMessage NewRequest(HttpMethod method, string relativePath)
    {
        var req = new HttpRequestMessage(method, _baseUrl + relativePath);
        if (!string.IsNullOrEmpty(_apiKey)) req.Headers.TryAddWithoutValidation("X-Api-Key", _apiKey);
        return req;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct) => await _http.SendAsync(req, ct);

    // ============== MINERS ==============
    public async Task<List<MinerDto>> GetMinersAsync(CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Get, "api/v1/miners");
        using var resp = await SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<List<MinerDto>>(JsonOpts, ct) ?? new List<MinerDto>();
    }

    public async Task<CommandResultDto> SendCommandAsync(string minerId, MinerCommandKind kind, int? poolId = null, CancellationToken ct = default)
    {
        var cmd = new MinerCommand(Guid.NewGuid().ToString("N"), minerId, kind, poolId);
        using var req = NewRequest(HttpMethod.Post, "api/v1/commands");
        req.Content = JsonContent.Create(cmd, options: JsonOpts);
        HttpResponseMessage resp;
        try { resp = await SendAsync(req, ct); }
        catch (HttpRequestException ex) { return new CommandResultDto(cmd.RequestId, minerId, false, "Server unreachable: " + ex.Message, DateTimeOffset.UtcNow); }
        using (resp)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            try { var dto = JsonSerializer.Deserialize<CommandResultDto>(body, JsonOpts); if (dto is not null) return dto; } catch { }
            return new CommandResultDto(cmd.RequestId, minerId, false, $"HTTP {(int)resp.StatusCode}: {body}", DateTimeOffset.UtcNow);
        }
    }

    // ============== SCHEDULES ==============
    public async Task<List<ScheduleDto>> GetSchedulesAsync(CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Get, "api/v1/schedules");
        using var resp = await SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<List<ScheduleDto>>(JsonOpts, ct) ?? new();
    }
    public async Task<long> CreateScheduleAsync(ScheduleCreateRequest body, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Post, "api/v1/schedules");
        req.Content = JsonContent.Create(body, options: JsonOpts);
        using var resp = await SendAsync(req, ct);
        var str = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {str}");
        using var doc = JsonDocument.Parse(str);
        return doc.RootElement.GetProperty("id").GetInt64();
    }
    public async Task DeleteScheduleAsync(long id, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Delete, $"api/v1/schedules/{id}");
        using var resp = await SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}");
    }
    public async Task EnableScheduleAsync(long id, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Post, $"api/v1/schedules/{id}/enable");
        using var resp = await SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }
    public async Task DisableScheduleAsync(long id, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Post, $"api/v1/schedules/{id}/disable");
        using var resp = await SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    // ============== POOLS ==============
    public async Task SetPoolsAsync(string minerId, List<PoolConfigDto> pools, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Post, $"api/v1/miners/{Uri.EscapeDataString(minerId)}/pools");
        req.Content = JsonContent.Create(new { Pools = pools }, options: JsonOpts);
        using var resp = await SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) { var body = await resp.Content.ReadAsStringAsync(ct); throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {body}"); }
    }

    // ============== DETAILED SNAPSHOT ==============
    public async Task<MinerDetailedSnapshot> GetDetailedSnapshotAsync(string minerId, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Get, $"api/v1/miners/{Uri.EscapeDataString(minerId)}/detailed");
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
        if (!resp.IsSuccessStatusCode) { var body = await resp.Content.ReadAsStringAsync(ct); throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {body}"); }
        return await resp.Content.ReadFromJsonAsync<MinerDetailedSnapshot>(JsonOpts, ct) ?? throw new InvalidOperationException("Empty detailed response");
    }

    // ============== METRICS ==============
    public async Task<MetricsHistoryDto> GetMetricsAsync(string minerId, TimeSpan window, TimeSpan step, CancellationToken ct = default)
    {
        var windowSec = (int)window.TotalSeconds;
        var stepSec = (int)step.TotalSeconds;
        using var req = NewRequest(HttpMethod.Get, $"api/v1/miners/{Uri.EscapeDataString(minerId)}/metrics?windowSec={windowSec}&stepSec={stepSec}");
        using var resp = await SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"Ошибка получения метрик: {resp.StatusCode}");
        return await resp.Content.ReadFromJsonAsync<MetricsHistoryDto>(JsonOpts, ct) ?? throw new InvalidOperationException("Empty metrics");
    }

    // ============== ALERTS ==============
    public async Task<List<AlertRuleDto>> GetAlertRulesAsync(CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Get, "api/v1/alerts/rules");
        using var resp = await SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<List<AlertRuleDto>>(JsonOpts, ct) ?? new();
    }
    public async Task<long> CreateAlertRuleAsync(AlertRuleCreateRequest body, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Post, "api/v1/alerts/rules");
        req.Content = JsonContent.Create(body, options: JsonOpts);
        using var resp = await SendAsync(req, ct);
        var str = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {str}");
        using var doc = JsonDocument.Parse(str);
        return doc.RootElement.GetProperty("id").GetInt64();
    }
    public async Task DeleteAlertRuleAsync(long id, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Delete, $"api/v1/alerts/rules/{id}");
        using var resp = await SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }
    public async Task EnableAlertRuleAsync(long id, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Post, $"api/v1/alerts/rules/{id}/enable");
        using var resp = await SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }
    public async Task DisableAlertRuleAsync(long id, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Post, $"api/v1/alerts/rules/{id}/disable");
        using var resp = await SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }
    public async Task<List<AlertEventDto>> GetAlertEventsAsync(int limit = 100, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Get, $"api/v1/alerts/events?limit={limit}");
        using var resp = await SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<List<AlertEventDto>>(JsonOpts, ct) ?? new();
    }

    // ============== AUTOACTIONS ==============
    public async Task<List<AutoActionDto>> GetAutoActionsAsync(CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Get, "api/v1/autoactions");
        using var resp = await SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<List<AutoActionDto>>(JsonOpts, ct) ?? new();
    }
    public async Task<long> CreateAutoActionAsync(AutoActionCreateRequest body, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Post, "api/v1/autoactions");
        req.Content = JsonContent.Create(body, options: JsonOpts);
        using var resp = await SendAsync(req, ct);
        var str = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {str}");
        using var doc = JsonDocument.Parse(str);
        return doc.RootElement.GetProperty("id").GetInt64();
    }
    public async Task DeleteAutoActionAsync(long id, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Delete, $"api/v1/autoactions/{id}");
        using var resp = await SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }
    public async Task EnableAutoActionAsync(long id, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Post, $"api/v1/autoactions/{id}/enable");
        using var resp = await SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }
    public async Task DisableAutoActionAsync(long id, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Post, $"api/v1/autoactions/{id}/disable");
        using var resp = await SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }
    public async Task<List<AutoActionEventDto>> GetAutoActionEventsAsync(int limit = 100, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Get, $"api/v1/autoactions/events?limit={limit}");
        using var resp = await SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<List<AutoActionEventDto>>(JsonOpts, ct) ?? new();
    }

    // ============== SETTINGS (Telegram) ==============
    public async Task<SettingsDto> GetSettingsAsync(CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Get, "api/v1/settings");
        using var resp = await SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SettingsDto>(JsonOpts, ct) ?? new SettingsDto();
    }
    public async Task SaveSettingsAsync(SettingsUpdateRequest body, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Put, "api/v1/settings");
        req.Content = JsonContent.Create(body, options: JsonOpts);
        using var resp = await SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) { var s = await resp.Content.ReadAsStringAsync(ct); throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {s}"); }
    }
    public async Task<SettingsTestResult> TestTelegramAsync(CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Post, "api/v1/settings/telegram/test");
        using var resp = await SendAsync(req, ct);
        var str = await resp.Content.ReadAsStringAsync(ct);
        try { var result = JsonSerializer.Deserialize<SettingsTestResult>(str, JsonOpts); if (result is not null) return result; } catch { }
        return new SettingsTestResult(false, $"HTTP {(int)resp.StatusCode}: {str}");
    }

    // ============== MINER SETTINGS (Target Temp, Preset Switcher) ==============
    public async Task SetTargetTempAsync(string minerId, int targetTemp, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Post, $"api/v1/miners/{Uri.EscapeDataString(minerId)}/settings/target-temp");
        req.Content = JsonContent.Create(new { TargetTemp = targetTemp }, options: JsonOpts);
        using var resp = await SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}");
    }
    public async Task ConfigurePresetSwitcherAsync(string minerId, bool enabled, string minPreset, string maxPreset, int riseTemp, int decreaseTemp, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Post, $"api/v1/miners/{Uri.EscapeDataString(minerId)}/settings/preset-switcher");
        req.Content = JsonContent.Create(new { Enabled = enabled, MinPreset = minPreset, MaxPreset = maxPreset, RiseTemp = riseTemp, DecreaseTemp = decreaseTemp }, options: JsonOpts);
        using var resp = await SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}");
    }

    // ============== PRESETS ==============
    public async Task<List<VnishAutotunePreset>> GetPresetsAsync(string minerId, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Get, $"api/v1/miners/{Uri.EscapeDataString(minerId)}/presets");
        using var resp = await SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}");
        return await resp.Content.ReadFromJsonAsync<List<VnishAutotunePreset>>(JsonOpts, ct) ?? new();
    }
}

public sealed record MinerDto(string MinerId, string Ip, string Mac, string Hostname, string Model, string ModelCode, string Firmware, string WatchdogId, DateTimeOffset FirstSeen, DateTimeOffset LastSeen, MinerSnapshot? Snapshot);
public sealed record WatchdogDto(string Id, string Name, DateTimeOffset FirstSeen, DateTimeOffset LastSeen, int TotalSnapshotsReceived);
public sealed record CommandResultDto(string RequestId, string MinerId, bool Success, string? Error, DateTimeOffset CompletedAt);
public sealed record MetricsPointDto(DateTimeOffset Time, double HashrateGhs, int PcbMaxTempC, int ChipMaxTempC, int FanDutyPercent, int PowerConsumptionW);
public sealed record MinerEventAnnotationDto(DateTimeOffset Time, string Kind, int? ChainId);
public sealed record MetricsHistoryDto(List<MetricsPointDto> Points, List<MinerEventAnnotationDto> Annotations)
{

    public async Task SetCoolingModeAsync(string minerId, string mode, int fanDuty, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Post, $"api/v1/miners/{Uri.EscapeDataString(minerId)}/settings/cooling-mode");
        req.Content = JsonContent.Create(new { Mode = mode, FanDuty = fanDuty }, options: JsonOpts);
        using var resp = await SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}");
    }

    public async Task SetQuietModeAsync(string minerId, bool enabled, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Post, $"api/v1/miners/{Uri.EscapeDataString(minerId)}/settings/quiet-mode");
        req.Content = JsonContent.Create(new { Enabled = enabled }, options: JsonOpts);
        using var resp = await SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}");
    }

    public async Task SetRestartTempAsync(string minerId, int temp, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Post, $"api/v1/miners/{Uri.EscapeDataString(minerId)}/settings/restart-temp");
        req.Content = JsonContent.Create(new { Temp = temp }, options: JsonOpts);
        using var resp = await SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}");
    }

    public async Task SetStartupDelayAsync(string minerId, int seconds, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Post, $"api/v1/miners/{Uri.EscapeDataString(minerId)}/settings/startup-delay");
        req.Content = JsonContent.Create(new { Seconds = seconds }, options: JsonOpts);
        using var resp = await SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}");
    }
};