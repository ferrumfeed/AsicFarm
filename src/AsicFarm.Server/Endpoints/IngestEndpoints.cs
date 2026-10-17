using System.Text.Json;
using AsicFarm.Server.Data;
using AsicFarm.Server.Hubs;
using AsicFarm.Server.Services;
using AsicFarm.Shared.Contracts;
using AsicFarm.Shared.Domain;
using AsicFarm.Shared.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AsicFarm.Server.Endpoints;

public static class IngestEndpoints
{
    public static void MapIngest(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/ingest/snapshots", HandleIngestAsync);
        app.MapPost("/api/v1/ingest/detailed", HandleDetailedIngestAsync);
    }

    private static async Task<IResult> HandleIngestAsync(
        IngestBatchRequest req,
        AsicFarmDbContext db,
        IHubContext<TelemetryHub> hub,
        ILogger<Program> log,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.WatchdogId))
            return Results.BadRequest(new { error = "watchdog_id required" });

        var now = DateTimeOffset.UtcNow;

        var wd = await db.Watchdogs.FirstOrDefaultAsync(w => w.Id == req.WatchdogId, ct);
        if (wd is null)
        {
            wd = new WatchdogEntity { Id = req.WatchdogId, Name = req.WatchdogId, FirstSeen = now };
            db.Watchdogs.Add(wd);
        }
        wd.LastSeen = now;
        wd.TotalSnapshotsReceived += req.Snapshots.Count;

        var accepted = 0;
        var rejected = 0;

        foreach (var snap in req.Snapshots)
        {
            if (string.IsNullOrWhiteSpace(snap.MinerId)) { rejected++; continue; }

            var miner = await db.Miners.FirstOrDefaultAsync(m => m.Id == snap.MinerId, ct);
            if (miner is null)
            {
                miner = new MinerEntity { Id = snap.MinerId, FirstSeen = snap.Timestamp };
                db.Miners.Add(miner);
            }

            miner.Ip = snap.IpAddress;
            miner.Mac = snap.MacAddress;
            miner.Hostname = snap.Hostname;
            miner.Model = snap.Model;
            miner.ModelCode = snap.ModelCode;
            miner.Algorithm = snap.Algorithm;
            miner.FirmwareName = snap.FirmwareName;
            miner.FirmwareVersion = snap.FirmwareVersion;
            miner.WatchdogId = req.WatchdogId;
            miner.LastSeen = snap.Timestamp;
            miner.LastSnapshotJson = JsonSerializer.Serialize(snap, WireJson.Options);
            accepted++;
        }

        await db.SaveChangesAsync(ct);

        // Шлём обновления в UI. Payload содержит ВСЁ, что нужно
        // для создания новой карточки, если майнер новый.
        foreach (var snap in req.Snapshots)
        {
            await hub.Clients.All.SendAsync("miner.updated", new
            {
                minerId = snap.MinerId,
                ip = snap.IpAddress,
                mac = snap.MacAddress,
                hostname = snap.Hostname,
                model = snap.Model,
                modelCode = snap.ModelCode,
                firmwareName = snap.FirmwareName,
                firmwareVersion = snap.FirmwareVersion,
                watchdogId = req.WatchdogId,
                state = snap.State.ToString(),
                hashrateGhs = snap.HashrateRealtimeGhs,
                hashratePercent = snap.HashratePercent,
                chipMaxTemp = snap.ChipTemp.Max,
                powerW = snap.PowerConsumptionW,
                timestamp = snap.Timestamp,
            }, ct);
        }

        log.LogInformation("Ingest from {Watchdog}: {Accepted} accepted, {Rejected} rejected",
            req.WatchdogId, accepted, rejected);

        return Results.Ok(new IngestBatchResponse(accepted, rejected, null));
    }

    /// Получение детального снапшота от ватчдога (ответ на RequestDetailedSnapshot).
    private static IResult HandleDetailedIngestAsync(
        DetailedIngestRequest req,
        DetailedSnapshotBroker broker,
        ILogger<Program> log)
    {
        if (string.IsNullOrWhiteSpace(req.RequestId))
            return Results.BadRequest(new { error = "requestId required" });

        if (!req.Success || req.Snapshot is null)
        {
            log.LogWarning("Detailed ingest failed for {RequestId}: {Error}",
                req.RequestId, req.Error);
            return Results.Ok(new { accepted = false });
        }

        log.LogInformation("Detailed snapshot received: {MinerId}, chains={Chains}",
            req.Snapshot.MinerId, req.Snapshot.ChainDetails.Count);

        broker.Complete(req.RequestId, req.Snapshot);
        return Results.Ok(new { accepted = true });
    }
}