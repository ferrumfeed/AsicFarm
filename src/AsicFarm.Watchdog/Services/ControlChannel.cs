using System.Collections.Concurrent;
using System.Net.Http.Json;
using AsicFarm.MinerClients.Vnish;
using AsicFarm.Shared.Contracts;
using AsicFarm.Shared.Exceptions;
using AsicFarm.Shared.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AsicFarm.Watchdog.Services;

public sealed class ControlChannel : BackgroundService
{
    private readonly WatchdogOptions _opt;
    private readonly MinerRegistry _registry;
    private readonly ILogger<ControlChannel> _log;
    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<string, VnishAntminerClient> _clients = new();
    private HubConnection? _conn;

    public ControlChannel(IOptions<WatchdogOptions> options, MinerRegistry registry, ILogger<ControlChannel> log)
    {
        _opt = options.Value;
        _registry = registry;
        _log = log;

        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(30),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(20),
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests,
        };

        _http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };

        if (!string.IsNullOrWhiteSpace(_opt.ServerUrl))
        {
            _http.BaseAddress = new Uri(_opt.ServerUrl.TrimEnd('/') + "/");
            if (!string.IsNullOrWhiteSpace(_opt.ServerApiKey))
                _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", _opt.ServerApiKey);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_opt.ServerUrl))
        {
            _log.LogWarning("ControlChannel: ServerUrl not set, disabled");
            return;
        }

        var url = _opt.ServerUrl.TrimEnd('/') + "/hubs/control?watchdogId=" + Uri.EscapeDataString(_opt.WatchdogId);

        _conn = new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                if (!string.IsNullOrWhiteSpace(_opt.ServerApiKey))
                    options.Headers.Add("X-Api-Key", _opt.ServerApiKey);
            })
            .WithServerTimeout(TimeSpan.FromMinutes(5))
            .WithKeepAliveInterval(TimeSpan.FromSeconds(15))
            .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
            .Build();

        _conn.On<ExecuteCommandMessage>("ExecuteCommand", async msg =>
        {
            if (msg.Command.Kind == MinerCommandKind.RequestDetailedSnapshot)
            {
                await HandleDetailedAsync(msg.Command, stoppingToken);
                return;
            }

            var result = await ExecuteAsync(msg.Command, stoppingToken);
            try { await _conn!.InvokeAsync("ReportResult", new CommandResultMessage(result), stoppingToken); }
            catch (Exception ex) { _log.LogWarning(ex, "ReportResult failed"); }
        });

        _conn.On<string, List<PoolConfigDto>>("SetPools", async (minerId, pools) =>
        {
            _log.LogInformation("SetPools RECEIVED: miner={MinerId}, pools={Count}", minerId, pools.Count);
            try
            {
                var ip = _registry.TryGetIp(minerId);
                if (ip == null) { _log.LogError("SetPools: ABORTED - miner {MinerId} not in registry", minerId); return; }
                var client = _clients.GetOrAdd(ip, CreateClient);
                await client.SetPoolsAsync(pools, stoppingToken);
                _log.LogInformation("SetPools: SUCCESS for {MinerId} ({Ip})", minerId, ip);
            }
            catch (Exception ex) { _log.LogError(ex, "SetPools: ERROR for {MinerId}", minerId); }
        });

        _conn.Reconnecting += _ => { _log.LogWarning("Control channel reconnecting..."); return Task.CompletedTask; };
        _conn.Reconnected += _ => { _log.LogInformation("Control channel reconnected"); return Task.CompletedTask; };

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _conn.StartAsync(stoppingToken);
                _log.LogInformation("Control channel connected as {WatchdogId}", _opt.WatchdogId);
                break;
            }
            catch (Exception ex)
            {
                _log.LogWarning("Control channel connect failed: {Msg}, retry in 10s", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) { }
    }

    private async Task HandleDetailedAsync(MinerCommand cmd, CancellationToken ct)
    {
        DetailedIngestRequest payload;
        try
        {
            var ip = _registry.TryGetIp(cmd.MinerId) ?? throw new InvalidOperationException($"Unknown miner '{cmd.MinerId}'");
            var client = _clients.GetOrAdd(ip, CreateClient);
            var snapshot = await client.GetFullDetailsAsync(ct);
            payload = new DetailedIngestRequest(cmd.RequestId, cmd.MinerId, true, null, snapshot);
        }
        catch (Exception ex) { payload = new DetailedIngestRequest(cmd.RequestId, cmd.MinerId, false, ex.Message, null); }

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var resp = await _http.PostAsJsonAsync("api/v1/ingest/detailed", payload, WireJson.Options, ct);
                if (resp.IsSuccessStatusCode) return;
                var body = await resp.Content.ReadAsStringAsync(ct);
                _log.LogWarning("Detailed POST rejected: HTTP {Code} {Body}", (int)resp.StatusCode, body);
                return;
            }
            catch (Exception) when (attempt < 3) { await Task.Delay(TimeSpan.FromSeconds(attempt * 3), ct); }
            catch (Exception ex) { _log.LogWarning(ex, "Detailed POST failed after 3 attempts"); }
        }
    }

    private async Task<MinerCommandResult> ExecuteAsync(MinerCommand cmd, CancellationToken ct)
    {
        _log.LogInformation("ExecuteAsync START: kind={Kind}, miner={MinerId}", cmd.Kind, cmd.MinerId);

        var result = new MinerCommandResult(cmd.RequestId, cmd.MinerId, false, "unknown error", DateTimeOffset.UtcNow);
        try
        {
            var ip = _registry.TryGetIp(cmd.MinerId) ?? throw new InvalidOperationException($"Unknown miner '{cmd.MinerId}'");
            var client = _clients.GetOrAdd(ip, CreateClient);

            switch (cmd.Kind)
            {
                case MinerCommandKind.Reboot: await client.RebootAsync(ct); break;
                case MinerCommandKind.StartMining: await client.StartMiningAsync(ct); break;
                case MinerCommandKind.StopMining: await client.StopMiningAsync(ct); break;
                case MinerCommandKind.RestartMining: await client.RestartMiningAsync(ct); break;
                case MinerCommandKind.PauseMining: await client.PauseMiningAsync(ct); break;
                case MinerCommandKind.ResumeMining: await client.ResumeMiningAsync(ct); break;
                case MinerCommandKind.SwitchPool:
                    if (cmd.PoolId is null) throw new InvalidOperationException("PoolId required");
                    await client.SwitchPoolAsync(cmd.PoolId.Value, ct); break;

                case MinerCommandKind.SetTargetTemp:
                    if (cmd.TargetTemp is null) throw new InvalidOperationException("TargetTemp required");
                    await client.SetTargetTemperatureAsync(cmd.TargetTemp.Value, ct);
                    break;

                case MinerCommandKind.SetCoolingMode:
                    if (cmd.CoolingMode is null) throw new InvalidOperationException("CoolingMode required");
                    await client.SetCoolingModeAsync(cmd.CoolingMode, cmd.FanDuty ?? 50, ct);
                    break;

                case MinerCommandKind.SetQuietMode:
                    if (cmd.Enabled is null) throw new InvalidOperationException("Enabled required");
                    await client.SetQuietModeAsync(cmd.Enabled.Value, ct);
                    break;

                case MinerCommandKind.SetRestartTemp:
                    if (cmd.Value is null) throw new InvalidOperationException("Value required");
                    await client.SetRestartTempAsync(cmd.Value.Value, ct);
                    break;

                case MinerCommandKind.SetStartupDelay:
                    if (cmd.Value is null) throw new InvalidOperationException("Value required");
                    await client.SetStartupDelayAsync(cmd.Value.Value, ct);
                    break;

                default: throw new InvalidOperationException($"Unknown command: {cmd.Kind}");
            }
            return result with { Success = true, Error = null, CompletedAt = DateTimeOffset.UtcNow };
        }
        catch (MinerAuthException) { return result with { Error = "Authorization required", CompletedAt = DateTimeOffset.UtcNow }; }
        catch (MinerUnreachableException) { return result with { Error = "Miner unreachable", CompletedAt = DateTimeOffset.UtcNow }; }
        catch (Exception ex) { _log.LogError(ex, "ExecuteAsync FAILED: kind={Kind}, miner={MinerId}", cmd.Kind, cmd.MinerId); return result with { Error = ex.Message, CompletedAt = DateTimeOffset.UtcNow }; }
    }

    private VnishAntminerClient CreateClient(string ip)
    {
        return new VnishAntminerClient(new VnishAntminerClientOptions { Host = ip, Password = _opt.Password });
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_conn is not null) await _conn.DisposeAsync();
        foreach (var c in _clients.Values) await c.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }
}