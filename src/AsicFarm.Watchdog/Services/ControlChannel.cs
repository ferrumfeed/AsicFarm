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

    public ControlChannel(
        IOptions<WatchdogOptions> options,
        MinerRegistry registry,
        ILogger<ControlChannel> log)
    {
        _opt = options.Value;
        _registry = registry;
        _log = log;

        // Свой HttpClient с явным SocketsHttpHandler.
        // ConnectTimeout 30 сек, ответ может ждать 3 минуты.
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(30),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(20),
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests,
        };

        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(3),
        };

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

        var url = _opt.ServerUrl.TrimEnd('/')
                  + "/hubs/control?watchdogId=" + Uri.EscapeDataString(_opt.WatchdogId);

        _conn = new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                if (!string.IsNullOrWhiteSpace(_opt.ServerApiKey))
                    options.Headers.Add("X-Api-Key", _opt.ServerApiKey);
            })
            .WithServerTimeout(TimeSpan.FromMinutes(5))
            .WithKeepAliveInterval(TimeSpan.FromSeconds(15))
            .WithAutomaticReconnect(new[]
            {
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(30),
            })
            .Build();

        _conn.On<ExecuteCommandMessage>("ExecuteCommand", async msg =>
        {
            if (msg.Command.Kind == MinerCommandKind.RequestDetailedSnapshot)
            {
                await HandleDetailedAsync(msg.Command, stoppingToken);
                return;
            }

            var result = await ExecuteAsync(msg.Command, stoppingToken);
            try
            {
                await _conn!.InvokeAsync("ReportResult",
                    new CommandResultMessage(result), stoppingToken);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "ReportResult failed");
            }
        });

        _conn.Reconnecting += _ =>
        {
            _log.LogWarning("Control channel reconnecting...");
            return Task.CompletedTask;
        };
        _conn.Reconnected += _ =>
        {
            _log.LogInformation("Control channel reconnected");
            return Task.CompletedTask;
        };

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

    /// Обработка RequestDetailedSnapshot:
    /// собираем снапшот локально, затем шлём его HTTP POST на сервер с retry.
    private async Task HandleDetailedAsync(MinerCommand cmd, CancellationToken ct)
    {
        DetailedIngestRequest payload;

        try
        {
            var ip = _registry.TryGetIp(cmd.MinerId)
                ?? throw new InvalidOperationException(
                    $"Unknown miner '{cmd.MinerId}' — попробуй позже");

            var client = _clients.GetOrAdd(ip, CreateClient);
            var snapshot = await client.GetFullDetailsAsync(ct);

            _log.LogInformation("Detailed snapshot for {Miner}: {Chains} chains, {Chips} chips",
                cmd.MinerId,
                snapshot.ChainDetails.Count,
                snapshot.ChainDetails.Sum(c => c.Chips.Count));

            payload = new DetailedIngestRequest(
                cmd.RequestId, cmd.MinerId, true, null, snapshot);
        }
        catch (MinerAuthException ex)
        {
            _log.LogWarning("Detailed {Miner}: AUTH {Msg}", cmd.MinerId, ex.Message);
            payload = new DetailedIngestRequest(cmd.RequestId, cmd.MinerId, false,
                "Authorization required. Задай Password в appsettings ватчдога.", null);
        }
        catch (MinerUnreachableException ex)
        {
            _log.LogWarning("Detailed {Miner}: UNREACHABLE {Msg}", cmd.MinerId, ex.Message);
            payload = new DetailedIngestRequest(cmd.RequestId, cmd.MinerId, false,
                "Miner unreachable", null);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Detailed {Miner} failed", cmd.MinerId);
            payload = new DetailedIngestRequest(cmd.RequestId, cmd.MinerId, false, ex.Message, null);
        }

        // 3 попытки отправить на сервер
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var resp = await _http.PostAsJsonAsync(
                    "api/v1/ingest/detailed", payload, WireJson.Options, ct);

                if (resp.IsSuccessStatusCode)
                {
                    _log.LogInformation(
                        "Detailed POST sent for {Miner} (attempt {Attempt}): {Code}",
                        cmd.MinerId, attempt, (int)resp.StatusCode);
                    return;
                }

                var body = await resp.Content.ReadAsStringAsync(ct);
                _log.LogWarning("Detailed POST rejected: HTTP {Code} {Body}",
                    (int)resp.StatusCode, body);
                return;
            }
            catch (Exception ex) when (attempt < 3)
            {
                _log.LogWarning("Detailed POST attempt {Attempt} failed: {Msg}, retry in {Sec}s",
                    attempt, ex.Message, attempt * 3);
                await Task.Delay(TimeSpan.FromSeconds(attempt * 3), ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Detailed POST failed after 3 attempts");
            }
        }
    }

    private async Task<MinerCommandResult> ExecuteAsync(MinerCommand cmd, CancellationToken ct)
    {
        var result = new MinerCommandResult(
            cmd.RequestId, cmd.MinerId, false, "unknown error", DateTimeOffset.UtcNow);

        try
        {
            var ip = _registry.TryGetIp(cmd.MinerId)
                ?? throw new InvalidOperationException(
                    $"Unknown miner '{cmd.MinerId}' — попробуй позже");

            var client = _clients.GetOrAdd(ip, CreateClient);

            switch (cmd.Kind)
            {
                case MinerCommandKind.Reboot:
                    await client.RebootAsync(ct); break;
                case MinerCommandKind.StartMining:
                    await client.StartMiningAsync(ct); break;
                case MinerCommandKind.StopMining:
                    await client.StopMiningAsync(ct); break;
                case MinerCommandKind.RestartMining:
                    await client.RestartMiningAsync(ct); break;
                case MinerCommandKind.PauseMining:
                    await client.PauseMiningAsync(ct); break;
                case MinerCommandKind.ResumeMining:
                    await client.ResumeMiningAsync(ct); break;
                case MinerCommandKind.SwitchPool:
                    if (cmd.PoolId is null)
                        throw new InvalidOperationException("PoolId required for SwitchPool");
                    await client.SwitchPoolAsync(cmd.PoolId.Value, ct); break;
                default:
                    throw new InvalidOperationException($"Unknown command: {cmd.Kind}");
            }

            _log.LogInformation("Command {Kind} on {Miner} ({Ip}): OK", cmd.Kind, cmd.MinerId, ip);
            return result with { Success = true, Error = null, CompletedAt = DateTimeOffset.UtcNow };
        }
        catch (MinerAuthException ex)
        {
            _log.LogWarning("Command {Kind} on {Miner}: AUTH {Msg}", cmd.Kind, cmd.MinerId, ex.Message);
            return result with { Error = "Authorization required", CompletedAt = DateTimeOffset.UtcNow };
        }
        catch (MinerUnreachableException ex)
        {
            _log.LogWarning("Command {Kind} on {Miner}: UNREACHABLE {Msg}", cmd.Kind, cmd.MinerId, ex.Message);
            return result with { Error = "Miner unreachable", CompletedAt = DateTimeOffset.UtcNow };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Command {Kind} on {Miner}: failed", cmd.Kind, cmd.MinerId);
            return result with { Error = ex.Message, CompletedAt = DateTimeOffset.UtcNow };
        }
    }

    private VnishAntminerClient CreateClient(string ip)
    {
        return new VnishAntminerClient(new VnishAntminerClientOptions
        {
            Host = ip,
            Password = _opt.Password,
        });
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_conn is not null) await _conn.DisposeAsync();
        foreach (var c in _clients.Values) await c.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }
}