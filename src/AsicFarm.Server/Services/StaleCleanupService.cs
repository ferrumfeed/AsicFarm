using AsicFarm.Server.Auth;
using AsicFarm.Server.Data;
using AsicFarm.Server.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AsicFarm.Server.Services;

/// Каждые 20 секунд чистит БД:
/// - удаляет майнеров, которые не слали снапшоты дольше MinerTtlMinutes
/// - удаляет ватчдогов, которые не выходят на связь дольше WatchdogTtlMinutes
/// И мгновенно пушит miner.removed в UI.
public sealed class StaleCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<TelemetryHub> _hub;
    private readonly ILogger<StaleCleanupService> _log;
    private readonly TimeSpan _minerTtl;
    private readonly TimeSpan _watchdogTtl;

    public StaleCleanupService(
        IServiceScopeFactory scopeFactory,
        IHubContext<TelemetryHub> hub,
        IOptions<ServerOptions> options,
        ILogger<StaleCleanupService> log)
    {
        _scopeFactory = scopeFactory;
        _hub = hub;
        _log = log;
        _minerTtl = TimeSpan.FromMinutes(Math.Max(1, options.Value.MinerTtlMinutes));
        _watchdogTtl = TimeSpan.FromMinutes(Math.Max(2, options.Value.WatchdogTtlMinutes));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation(
            "StaleCleanupService started: miner TTL = {MinerTtl}, watchdog TTL = {WatchdogTtl}, tick = 20s",
            _minerTtl, _watchdogTtl);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));

        try { await CleanupAsync(stoppingToken); }
        catch (Exception ex) { _log.LogError(ex, "Cleanup failed"); }

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await CleanupAsync(stoppingToken); }
            catch (Exception ex) { _log.LogError(ex, "Cleanup failed"); }
        }
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AsicFarmDbContext>();

        var now = DateTimeOffset.UtcNow;
        var minerCutoff = now - _minerTtl;
        var watchdogCutoff = now - _watchdogTtl;

        // SQLite не поддерживает сравнение DateTimeOffset в WHERE —
        // тянем всё в память и фильтруем на клиенте.
        var allMiners = await db.Miners.ToListAsync(ct);
        var staleMiners = allMiners.Where(m => m.LastSeen < minerCutoff).ToList();

        if (staleMiners.Count > 0)
        {
            db.Miners.RemoveRange(staleMiners);
            await db.SaveChangesAsync(ct);

            foreach (var m in staleMiners)
            {
                await _hub.Clients.All.SendAsync("miner.removed",
                    new { minerId = m.Id, reason = "stale", lastSeen = m.LastSeen }, ct);

                _log.LogInformation("Miner removed (stale): {Miner} ({Ip}), lastSeen {Last}",
                    m.Id, m.Ip, m.LastSeen);
            }
        }

        var allWatchdogs = await db.Watchdogs.ToListAsync(ct);
        var staleWatchdogs = allWatchdogs.Where(w => w.LastSeen < watchdogCutoff).ToList();

        if (staleWatchdogs.Count > 0)
        {
            foreach (var wd in staleWatchdogs)
            {
                // Каскадно — все его майнеры (если ещё остались)
                var miners = await db.Miners
                    .Where(m => m.WatchdogId == wd.Id)
                    .ToListAsync(ct);

                foreach (var m in miners)
                {
                    db.Miners.Remove(m);
                    await _hub.Clients.All.SendAsync("miner.removed",
                        new { minerId = m.Id, reason = "watchdog-gone", lastSeen = m.LastSeen }, ct);
                }

                db.Watchdogs.Remove(wd);
                _log.LogInformation("Watchdog removed (stale): {Id}", wd.Id);
            }

            await db.SaveChangesAsync(ct);
        }
    }
}