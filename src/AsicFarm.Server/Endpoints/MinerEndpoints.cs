using System.Text.Json;
using AsicFarm.Server.Data;
using AsicFarm.Server.Hubs;
using AsicFarm.Server.Services;
using AsicFarm.Shared.Domain;
using AsicFarm.Shared.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AsicFarm.Server.Endpoints;

public static class MinerEndpoints
{
    public static void MapMiners(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTimeOffset.UtcNow }));
        app.MapGet("/api/v1/miners", GetMinersAsync);
        app.MapGet("/api/v1/miners/{id}", GetMinerAsync);
        app.MapGet("/api/v1/miners/{id}/detailed", GetDetailedAsync);

        // НОВЫЙ ЭНДПОИНТ ДЛЯ МЕТРИК
        app.MapGet("/api/v1/miners/{id}/metrics", GetMetricsAsync);

        app.MapGet("/api/v1/watchdogs", GetWatchdogsAsync);
        app.MapDelete("/api/v1/watchdogs/{id}", DeleteWatchdogAsync);
    }

    private static async Task<IResult> GetMinersAsync(
        AsicFarmDbContext db,
        string? watchdogId,
        int? onlineWithinSeconds,
        CancellationToken ct)
    {
        var q = db.Miners.AsQueryable();

        if (!string.IsNullOrWhiteSpace(watchdogId))
            q = q.Where(m => m.WatchdogId == watchdogId);

        if (onlineWithinSeconds is { } sec)
        {
            var cutoff = DateTimeOffset.UtcNow.AddSeconds(-sec);
            q = q.Where(m => m.LastSeen >= cutoff);
        }

        var miners = await q.ToListAsync(ct);

        var result = miners.Select(m => new
        {
            minerId = m.Id,
            ip = m.Ip,
            mac = m.Mac,
            hostname = m.Hostname,
            model = m.Model,
            modelCode = m.ModelCode,
            firmware = m.FirmwareName + " " + m.FirmwareVersion,
            watchdogId = m.WatchdogId,
            firstSeen = m.FirstSeen,
            lastSeen = m.LastSeen,
            snapshot = TryDeserialize(m.LastSnapshotJson),
        }).ToList();

        return Results.Ok(result);
    }

    private static async Task<IResult> GetMinerAsync(string id, AsicFarmDbContext db, CancellationToken ct)
    {
        var m = await db.Miners.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return Results.NotFound();

        return Results.Ok(new
        {
            minerId = m.Id,
            ip = m.Ip,
            mac = m.Mac,
            hostname = m.Hostname,
            model = m.Model,
            modelCode = m.ModelCode,
            firmware = m.FirmwareName + " " + m.FirmwareVersion,
            watchdogId = m.WatchdogId,
            firstSeen = m.FirstSeen,
            lastSeen = m.LastSeen,
            snapshot = TryDeserialize(m.LastSnapshotJson),
        });
    }

    // НОВЫЙ МЕТОД-ЗАГЛУШКА ДЛЯ ГРАФИКОВ
    private static IResult GetMetricsAsync(string id, int windowSec, int stepSec)
    {
        var random = new Random(id.GetHashCode());
        var now = DateTimeOffset.UtcNow;
        var points = new List<MetricsPointDto>();
        var events = new List<MinerEventAnnotationDto>();

        int pointsCount = Math.Clamp(windowSec / Math.Max(stepSec, 60), 10, 1000);
        for (int i = pointsCount; i >= 0; i--)
        {
            var time = now.AddSeconds(-i * stepSec);
            points.Add(new MetricsPointDto(
                time,
                180000 + random.Next(-8000, 8000), // Хешрейт GH/s
                65,
                65 + random.Next(-3, 8),           // Температура
                50,
                4000 + random.Next(-200, 200)
            ));
        }
        events.Add(new MinerEventAnnotationDto(now.AddHours(-12), "start", null));
        events.Add(new MinerEventAnnotationDto(now.AddHours(-6), "restart", null));

        return Results.Ok(new MetricsHistoryDto(points, events));
    }

    private static async Task<IResult> GetDetailedAsync(
        string id,
        AsicFarmDbContext db,
        IHubContext<ControlHub> hub,
        DetailedSnapshotBroker broker,
        ILogger<Program> log,
        CancellationToken ct)
    {
        var m = await db.Miners.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return Results.NotFound(new { error = $"Miner '{id}' not found" });

        if (string.IsNullOrWhiteSpace(m.WatchdogId))
            return Results.BadRequest(new { error = "Miner has no watchdog bound" });

        var requestId = Guid.NewGuid().ToString("N");
        var tcs = broker.Register(requestId);

        log.LogInformation("Detailed request {RequestId} for {Miner} from {Watchdog}",
            requestId, id, m.WatchdogId);

        try
        {
            var cmd = new AsicFarm.Shared.Contracts.MinerCommand(
                requestId, id,
                AsicFarm.Shared.Contracts.MinerCommandKind.RequestDetailedSnapshot);

            await hub.Clients
                .Group(ControlHub.GroupName(m.WatchdogId))
                .SendAsync("ExecuteCommand",
                    new AsicFarm.Shared.Contracts.ExecuteCommandMessage(cmd), ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(90));

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, timeoutCts.Token));

            if (completed == tcs.Task)
            {
                var snapshot = await tcs.Task;
                return Results.Ok(snapshot);
            }

            broker.Remove(requestId);
            log.LogWarning("Detailed request {RequestId} timeout", requestId);
            return Results.Json(
                new { error = "Timeout ожидания детального снапшота (90 сек)" },
                statusCode: 504);
        }
        catch (Exception ex)
        {
            broker.Remove(requestId);
            log.LogError(ex, "Detailed request {RequestId} failed", requestId);
            return Results.Json(new { error = ex.Message }, statusCode: 500);
        }
    }

    private static async Task<IResult> GetWatchdogsAsync(AsicFarmDbContext db, CancellationToken ct)
    {
        var list = await db.Watchdogs.ToListAsync(ct);
        var sorted = list.OrderByDescending(w => w.LastSeen).Select(w => new
        {
            id = w.Id,
            name = w.Name,
            firstSeen = w.FirstSeen,
            lastSeen = w.LastSeen,
            totalSnapshotsReceived = w.TotalSnapshotsReceived,
        }).ToList();

        return Results.Ok(sorted);
    }

    private static async Task<IResult> DeleteWatchdogAsync(
        string id,
        AsicFarmDbContext db,
        IHubContext<TelemetryHub> hub,
        ILogger<Program> log,
        CancellationToken ct)
    {
        var wd = await db.Watchdogs.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (wd is null) return Results.NotFound();

        var miners = await db.Miners.Where(m => m.WatchdogId == id).ToListAsync(ct);

        foreach (var m in miners)
        {
            await hub.Clients.All.SendAsync("miner.removed",
                new { minerId = m.Id, reason = "watchdog-deleted", lastSeen = m.LastSeen }, ct);
        }

        db.Miners.RemoveRange(miners);
        db.Watchdogs.Remove(wd);
        await db.SaveChangesAsync(ct);

        log.LogInformation("Watchdog {Id} deleted manually with {Count} miners", id, miners.Count);
        return Results.Ok(new { deletedMiners = miners.Count });
    }

    private static MinerSnapshot? TryDeserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<MinerSnapshot>(json, WireJson.Options); }
        catch { return null; }
    }
}

// DTO для метрик (можно вынести в Shared)
public sealed record MetricsPointDto(DateTimeOffset Time, double HashrateGhs, int PcbMaxTempC, int ChipMaxTempC, int FanDutyPercent, int PowerConsumptionW);
public sealed record MinerEventAnnotationDto(DateTimeOffset Time, string Kind, int? ChainId);
public sealed record MetricsHistoryDto(List<MetricsPointDto> Points, List<MinerEventAnnotationDto> Annotations);