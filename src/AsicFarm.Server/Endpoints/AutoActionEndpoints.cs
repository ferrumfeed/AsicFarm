using AsicFarm.Server.Data;
using AsicFarm.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AsicFarm.Server.Endpoints;

public static class AutoActionEndpoints
{
    public static void MapAutoActions(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/autoactions", ListAsync);
        app.MapPost("/api/v1/autoactions", CreateAsync);
        app.MapDelete("/api/v1/autoactions/{id}", DeleteAsync);
        app.MapPost("/api/v1/autoactions/{id}/enable", EnableAsync);
        app.MapPost("/api/v1/autoactions/{id}/disable", DisableAsync);
        app.MapGet("/api/v1/autoactions/events", ListEventsAsync);
    }

    private static async Task<IResult> ListAsync(AsicFarmDbContext db, CancellationToken ct)
    {
        var list = await db.AutoActions.ToListAsync(ct);
        return Results.Ok(list.Select(ToDto).ToList());
    }

    private static async Task<IResult> CreateAsync(
        AutoActionCreateRequest req, AsicFarmDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return Results.BadRequest(new { error = "name required" });

        var entity = new AutoActionEntity
        {
            Name = req.Name.Trim(),
            Trigger = req.Trigger.ToString(),
            Threshold = req.Threshold,
            ForSeconds = Math.Max(30, req.ForSeconds),
            Action = req.Action.ToString(),
            Target = string.IsNullOrWhiteSpace(req.Target) ? "All" : req.Target,
            CooldownSeconds = Math.Max(60, req.CooldownSeconds),
            Enabled = req.Enabled,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.AutoActions.Add(entity);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new { id = entity.Id });
    }

    private static async Task<IResult> DeleteAsync(long id, AsicFarmDbContext db, CancellationToken ct)
    {
        var r = await db.AutoActions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return Results.NotFound();
        db.AutoActions.Remove(r);
        await db.SaveChangesAsync(ct);
        return Results.Ok();
    }

    private static async Task<IResult> EnableAsync(long id, AsicFarmDbContext db, CancellationToken ct)
    {
        var r = await db.AutoActions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return Results.NotFound();
        r.Enabled = true;
        await db.SaveChangesAsync(ct);
        return Results.Ok();
    }

    private static async Task<IResult> DisableAsync(long id, AsicFarmDbContext db, CancellationToken ct)
    {
        var r = await db.AutoActions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return Results.NotFound();
        r.Enabled = false;
        await db.SaveChangesAsync(ct);
        return Results.Ok();
    }

    private static async Task<IResult> ListEventsAsync(
        AsicFarmDbContext db, int? limit, CancellationToken ct)
    {
        var take = Math.Clamp(limit ?? 100, 1, 500);

        var all = await db.AutoActionEvents
            .Select(e => new AutoActionEventDto(
                e.Id, e.RuleId, e.RuleName, e.MinerId, e.MinerIp,
                e.Action, e.FiredAt, e.Success, e.Error))
            .ToListAsync(ct);

        var list = all
            .OrderByDescending(e => e.FiredAt)
            .Take(take)
            .ToList();

        return Results.Ok(list);
    }

    private static AutoActionDto ToDto(AutoActionEntity r)
    {
        var trigger = Enum.TryParse<AutoActionTrigger>(r.Trigger, out var t)
            ? t : AutoActionTrigger.HashrateBelow;
        var action = Enum.TryParse<AutoActionKind>(r.Action, out var a)
            ? a : AutoActionKind.RestartMining;

        return new AutoActionDto(
            r.Id, r.Name, trigger, r.Threshold, r.ForSeconds,
            action, r.Target, r.CooldownSeconds, r.Enabled,
            r.CreatedAt, r.LastFiredAt);
    }
}