using AsicFarm.Server.Data;
using AsicFarm.Server.Services;
using AsicFarm.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AsicFarm.Server.Endpoints;

public static class AlertEndpoints
{
    public static void MapAlerts(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/alerts/rules", ListRulesAsync);
        app.MapPost("/api/v1/alerts/rules", CreateRuleAsync);
        app.MapDelete("/api/v1/alerts/rules/{id}", DeleteRuleAsync);
        app.MapPost("/api/v1/alerts/rules/{id}/enable", EnableRuleAsync);
        app.MapPost("/api/v1/alerts/rules/{id}/disable", DisableRuleAsync);
        app.MapGet("/api/v1/alerts/events", ListEventsAsync);
        app.MapPost("/api/v1/alerts/test", TestTelegramAsync);
    }

    private static async Task<IResult> ListRulesAsync(AsicFarmDbContext db, CancellationToken ct)
    {
        var list = await db.AlertRules.ToListAsync(ct);
        return Results.Ok(list.Select(ToDto).ToList());
    }

    private static async Task<IResult> CreateRuleAsync(
        AlertRuleCreateRequest req, AsicFarmDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return Results.BadRequest(new { error = "name required" });

        var entity = new AlertRuleEntity
        {
            Name = req.Name.Trim(),
            Type = req.Type.ToString(),
            Target = req.Target ?? "All",
            Threshold = req.Threshold,
            Extra = req.Extra,
            ForSeconds = Math.Max(30, req.ForSeconds),
            CooldownSeconds = Math.Max(60, req.CooldownSeconds),
            Enabled = req.Enabled,
            Channel = string.IsNullOrWhiteSpace(req.Channel) ? "Telegram" : req.Channel,
            ChannelConfig = req.ChannelConfig,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.AlertRules.Add(entity);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new { id = entity.Id });
    }

    private static async Task<IResult> DeleteRuleAsync(long id, AsicFarmDbContext db, CancellationToken ct)
    {
        var r = await db.AlertRules.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return Results.NotFound();
        db.AlertRules.Remove(r);
        await db.SaveChangesAsync(ct);
        return Results.Ok();
    }

    private static async Task<IResult> EnableRuleAsync(long id, AsicFarmDbContext db, CancellationToken ct)
    {
        var r = await db.AlertRules.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return Results.NotFound();
        r.Enabled = true;
        await db.SaveChangesAsync(ct);
        return Results.Ok();
    }

    private static async Task<IResult> DisableRuleAsync(long id, AsicFarmDbContext db, CancellationToken ct)
    {
        var r = await db.AlertRules.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return Results.NotFound();
        r.Enabled = false;
        await db.SaveChangesAsync(ct);
        return Results.Ok();
    }

    private static async Task<IResult> ListEventsAsync(
        AsicFarmDbContext db, int? limit, CancellationToken ct)
    {
        var take = Math.Clamp(limit ?? 100, 1, 500);

        // SQLite не поддерживает ORDER BY по DateTimeOffset — сортируем в памяти.
        var all = await db.AlertEvents
            .Select(e => new AlertEventDto(
                e.Id, e.RuleId, e.RuleName, e.MinerId, e.MinerIp,
                e.FiredAt, e.Message, e.Delivered, e.DeliveryError))
            .ToListAsync(ct);

        var list = all
            .OrderByDescending(e => e.FiredAt)
            .Take(take)
            .ToList();

        return Results.Ok(list);
    }

    private static async Task<IResult> TestTelegramAsync(
        TelegramNotifier telegram, CancellationToken ct)
    {
        var (ok, err) = await telegram.SendAsync(null,
            "✅ <b>AsicFarm</b>: тестовое сообщение. Если ты это видишь — Telegram настроен.",
            ct);

        return ok
            ? Results.Ok(new SettingsTestResult(true, null))
            : Results.Json(new SettingsTestResult(false, err), statusCode: 500);
    }

    private static AlertRuleDto ToDto(AlertRuleEntity r)
    {
        var type = Enum.TryParse<AlertRuleType>(r.Type, out var t) ? t : AlertRuleType.MinerOffline;
        return new AlertRuleDto(
            r.Id, r.Name, type, r.Target, r.Threshold, r.Extra,
            r.ForSeconds, r.CooldownSeconds, r.Enabled,
            r.Channel, r.ChannelConfig, r.CreatedAt);
    }
}