using System.Collections.Concurrent;
using AsicFarm.MinerClients.Vnish;
using AsicFarm.Shared.Contracts;
using AsicFarm.Shared.Domain;
using AsicFarm.Shared.Exceptions;
using AsicFarm.Watchdog.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AsicFarm.Watchdog;

public sealed class MinerPoller : BackgroundService
{
    private readonly ILogger<MinerPoller> _log;
    private readonly WatchdogOptions _opt;
    private readonly MinerRegistry _registry;
    private readonly MinerHostRegistry _hostRegistry;
    private readonly ServerClient _server;
    private readonly ConcurrentDictionary<string, IAntminerClient> _clients = new();
    private readonly ConcurrentDictionary<string, MinerSnapshot> _lastSnapshots = new();

    public MinerPoller(
        IOptions<WatchdogOptions> options,
        ILogger<MinerPoller> log,
        MinerRegistry registry,
        MinerHostRegistry hostRegistry,
        ServerClient server)
    {
        _opt = options.Value;
        _log = log;
        _registry = registry;
        _hostRegistry = hostRegistry;
        _server = server;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("Watchdog started. Interval: {Interval}, rescan every {Rescan} min",
            _opt.PollInterval, _opt.RescanIntervalMinutes);

        using var timer = new PeriodicTimer(_opt.PollInterval);
        await PollAllAsync(stoppingToken).ConfigureAwait(false);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await PollAllAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task PollAllAsync(CancellationToken ct)
    {
        var hosts = _hostRegistry.AllHosts;
        if (hosts.Count == 0)
        {
            _log.LogDebug("Реестр хостов пуст — пропускаю тик");
            return;
        }

        var parallel = new ParallelOptions
        {
            MaxDegreeOfParallelism = _opt.MaxConcurrency,
            CancellationToken = ct,
        };

        var collected = new ConcurrentBag<MinerSnapshot>();

        await Parallel.ForEachAsync(hosts, parallel, async (host, innerCt) =>
        {
            var snap = await PollOneAsync(host, innerCt).ConfigureAwait(false);
            if (snap is not null)
            {
                collected.Add(snap);
                _hostRegistry.MarkAlive(host);
            }
        }).ConfigureAwait(false);

        if (collected.Count > 0)
        {
            try
            {
                await _server.SendBatchAsync(collected.ToList(), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogWarning("Send batch failed: {Msg}", ex.Message);
            }
        }
    }

    private async Task<MinerSnapshot?> PollOneAsync(string host, CancellationToken ct)
    {
        var client = _clients.GetOrAdd(host, CreateClient);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_opt.OperationTimeout);
        var opCt = timeoutCts.Token;

        try
        {
            var snap = await client.GetSnapshotAsync(opCt).ConfigureAwait(false);
            _lastSnapshots[host] = snap;

            if (!string.IsNullOrWhiteSpace(snap.MacAddress))
                _registry.Register(snap.MacAddress, snap.IpAddress);

            if (_opt.VerboseSnapshotLog)
            {
                _log.LogInformation(
                    "{Host} | {Model} | {State} | {Hr:F0} GH/s ({Pct:F1}%) | chip {ChipMin}..{ChipMax}C | {Power}W | {Fans} fans {Duty}%",
                    host, snap.Model, snap.State,
                    snap.HashrateRealtimeGhs, snap.HashratePercent,
                    snap.ChipTemp.Min, snap.ChipTemp.Max,
                    snap.PowerConsumptionW,
                    snap.Cooling.FanCount, snap.Cooling.FanDutyPercent);
            }

            return snap;
        }
        catch (MinerUnreachableException ex)
        {
            _log.LogWarning("{Host} | UNREACHABLE | {Msg}", host, ex.InnerException?.Message ?? ex.Message);
        }
        catch (MinerAuthException ex)
        {
            _log.LogWarning("{Host} | AUTH | {Msg}", host, ex.Message);
        }
        catch (MinerApiException ex)
        {
            _log.LogWarning("{Host} | API {Code} | {Msg}", host, ex.StatusCode, ex.Message);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _log.LogWarning("{Host} | TIMEOUT after {Timeout}", host, _opt.OperationTimeout);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "{Host} | unexpected error", host);
        }

        return null;
    }

    private IAntminerClient CreateClient(string host)
    {
        var (hostname, port) = ParseHost(host);
        return new VnishAntminerClient(new VnishAntminerClientOptions
        {
            Host = hostname,
            Port = port,
            Password = _opt.Password,
        });
    }

    private static (string host, int port) ParseHost(string raw)
    {
        var colon = raw.LastIndexOf(':');
        if (colon > 0 && int.TryParse(raw.AsSpan(colon + 1), out var p))
            return (raw[..colon], p);
        return (raw, 80);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var c in _clients.Values)
            await c.DisposeAsync().ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}