using System.Text.Json;
using AsicFarm.Server.Data;
using AsicFarm.Server.Hubs;
using AsicFarm.Shared.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AsicFarm.Server.Services;

public sealed class ScheduleRunner : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<ControlHub> _hub;
    private readonly ILogger<ScheduleRunner> _log;

    private DateTimeOffset _lastTick;

    public ScheduleRunner(
        IServiceScopeFactory scopeFactory,
        IHubContext<ControlHub> hub,
        ILogger<ScheduleRunner> log)
    {
        _scopeFactory = scopeFactory;
        _hub = hub;
        _log = log;
        _lastTick = DateTimeOffset.UtcNow;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("ScheduleRunner started, tick every 20s");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) { _log.LogError(ex, "ScheduleRunner tick failed"); }

            try { await timer.WaitForNextTickAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var windowStart = _lastTick;
        var windowEnd = nowUtc;
        _lastTick = nowUtc;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AsicFarmDbContext>();

        var schedules = await db.Schedules.Where(s => s.Enabled).ToListAsync(ct);
        if (schedules.Count == 0) return;

        foreach (var s in schedules)
        {
            List<DayAction>? actions;
            try
            {
                actions = JsonSerializer.Deserialize<List<DayAction>>(
                    s.ActionsJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            }
            catch (Exception ex)
            {
                _log.LogWarning("Schedule {Id}: invalid ActionsJson: {Msg}", s.Id, ex.Message);
                continue;
            }

            if (actions is null || actions.Count == 0) continue;

            if (!TzUtil.TryResolve(s.TimeZoneId, out var tz))
            {
                _log.LogWarning("Schedule {Id}: unknown TZ '{Tz}', fallback to UTC",
                    s.Id, s.TimeZoneId);
                tz = TimeZoneInfo.Utc;
            }

            var localStart = TimeZoneInfo.ConvertTime(windowStart, tz);
            var localEnd = TimeZoneInfo.ConvertTime(windowEnd, tz);

            if (localStart.Date != localEnd.Date) continue;

            var today = localEnd.DayOfWeek;

            foreach (var a in actions)
            {
                if (a.Day != today) continue;
                if (a.Action == ScheduleAction.None) continue;
                if (!TryParseHm(a.Time, out var h, out var m)) continue;

                var moment = new DateTimeOffset(
                    localEnd.Date.AddHours(h).AddMinutes(m),
                    localEnd.Offset);

                if (moment > localStart && moment <= localEnd)
                {
                    await FireAsync(db, s, a, ct);
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static bool TryParseHm(string? s, out int h, out int m)
    {
        h = m = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var parts = s.Split(':');
        if (parts.Length != 2) return false;
        return int.TryParse(parts[0], out h) && int.TryParse(parts[1], out m)
               && h is >= 0 and <= 23 && m is >= 0 and <= 59;
    }

    private async Task FireAsync(
        AsicFarmDbContext db, ScheduleEntity s, DayAction a, CancellationToken ct)
    {
        _log.LogInformation("Firing schedule {Id} '{Name}': {Day} {Time} → {Action}",
            s.Id, s.Name, a.Day, a.Time, a.Action);

        var kind = a.Action switch
        {
            ScheduleAction.StartMining => MinerCommandKind.StartMining,
            ScheduleAction.StopMining => MinerCommandKind.StopMining,
            ScheduleAction.RestartMining => MinerCommandKind.RestartMining,
            ScheduleAction.Reboot => MinerCommandKind.Reboot,
            _ => (MinerCommandKind?)null,
        };
        if (kind is null) return;

        var targets = await ResolveTargetsAsync(db, s, ct);

        var ok = 0;
        var fail = 0;
        string? firstError = null;

        foreach (var m in targets)
        {
            if (string.IsNullOrWhiteSpace(m.WatchdogId)) { fail++; continue; }

            var cmd = new MinerCommand(
                RequestId: Guid.NewGuid().ToString("N"),
                MinerId: m.Id,
                Kind: kind.Value);

            var r = await ControlHub.DispatchAsync(
                _hub, m.WatchdogId, cmd, TimeSpan.FromSeconds(30), ct);

            if (r.Success) ok++;
            else { fail++; firstError ??= r.Error; }
        }

        db.ScheduleRuns.Add(new ScheduleRunEntity
        {
            ScheduleId = s.Id,
            RunAt = DateTimeOffset.UtcNow,
            Description = $"{a.Day} {a.Time} → {a.Action}",
            TotalTargets = targets.Count,
            Succeeded = ok,
            Failed = fail,
            Error = firstError,
        });

        s.LastRunAt = DateTimeOffset.UtcNow;
    }

    private static async Task<List<MinerEntity>> ResolveTargetsAsync(
        AsicFarmDbContext db, ScheduleEntity s, CancellationToken ct)
    {
        return s.TargetKind switch
        {
            "Miner" => await db.Miners.Where(m => m.Id == s.TargetId).ToListAsync(ct),
            "Watchdog" => await db.Miners.Where(m => m.WatchdogId == s.TargetId).ToListAsync(ct),
            "All" => await db.Miners.ToListAsync(ct),
            _ => new List<MinerEntity>(),
        };
    }
}