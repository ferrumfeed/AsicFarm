using System.Text.Json;
using AsicFarm.Server.Data;
using AsicFarm.Server.Services;
using AsicFarm.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AsicFarm.Server.Endpoints;

public static class ScheduleEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static readonly string[] TimeZones =
    {
        "UTC",
        "Europe/Kaliningrad", "Europe/Moscow", "Europe/Samara",
        "Asia/Yekaterinburg", "Asia/Omsk", "Asia/Krasnoyarsk",
        "Asia/Irkutsk", "Asia/Yakutsk", "Asia/Vladivostok",
        "Asia/Magadan", "Asia/Kamchatka",
        "Asia/Almaty", "Asia/Tbilisi", "Asia/Baku", "Asia/Yerevan",
        "Europe/Minsk", "Europe/Kyiv",
        "Europe/Berlin", "Europe/London",
        "Asia/Dubai", "Asia/Shanghai",
        "America/New_York",
    };

    public static void MapSchedules(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/schedules", ListAsync);
        app.MapPost("/api/v1/schedules", CreateAsync);
        app.MapDelete("/api/v1/schedules/{id}", DeleteAsync);
        app.MapPost("/api/v1/schedules/{id}/enable", EnableAsync);
        app.MapPost("/api/v1/schedules/{id}/disable", DisableAsync);
        app.MapGet("/api/v1/schedules/{id}/runs", RunsAsync);
        app.MapGet("/api/v1/schedules/timezones", () => Results.Ok(TimeZones));
    }

    private static async Task<IResult> ListAsync(AsicFarmDbContext db, CancellationToken ct)
    {
        var list = await db.Schedules.ToListAsync(ct);
        return Results.Ok(list.Select(ToDto).ToList());
    }

    private static ScheduleDto ToDto(ScheduleEntity s)
    {
        List<DayAction> actions;
        try { actions = JsonSerializer.Deserialize<List<DayAction>>(s.ActionsJson, JsonOpts) ?? new(); }
        catch { actions = new(); }

        var targetKind = Enum.TryParse<ScheduleTargetKind>(s.TargetKind, out var tk)
            ? tk : ScheduleTargetKind.Miner;

        return new ScheduleDto(
            Id: s.Id, Name: s.Name, TargetKind: targetKind, TargetId: s.TargetId,
            Enabled: s.Enabled,
            TimeZoneId: TzUtil.ToIana(s.TimeZoneId),
            Actions: actions,
            CreatedAt: s.CreatedAt, LastRunAt: s.LastRunAt,
            NextEventDescription: ComputeNextEvent(actions, s.TimeZoneId));
    }

    private static string? ComputeNextEvent(IReadOnlyList<DayAction> actions, string tzId)
    {
        if (actions.Count == 0) return null;

        if (!TzUtil.TryResolve(tzId, out var tz))
            tz = TimeZoneInfo.Utc;

        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
        TimeSpan? bestDelta = null;
        string? bestDesc = null;

        for (var dayOffset = 0; dayOffset < 8; dayOffset++)
        {
            var probeDate = now.Date.AddDays(dayOffset);
            var dow = probeDate.DayOfWeek;

            foreach (var a in actions)
            {
                if (a.Day != dow) continue;
                if (a.Action == ScheduleAction.None) continue;
                if (!TryParseHm(a.Time, out var hh, out var mm)) continue;

                var ts = new DateTimeOffset(probeDate.AddHours(hh).AddMinutes(mm), now.Offset);
                var delta = ts - now;

                if (delta > TimeSpan.Zero && (bestDelta is null || delta < bestDelta))
                {
                    bestDelta = delta;
                    bestDesc = $"{Ru(dow)} {a.Time} → {ActionRu(a.Action)} (через {Human(delta)})";
                }
            }
        }

        return bestDesc;
    }

    private static string Human(TimeSpan t)
    {
        if (t.TotalMinutes < 60) return $"{(int)t.TotalMinutes} мин";
        if (t.TotalHours < 24) return $"{(int)t.TotalHours}ч {t.Minutes}м";
        return $"{(int)t.TotalDays}д {t.Hours}ч";
    }

    private static string Ru(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => "Пн",
        DayOfWeek.Tuesday => "Вт",
        DayOfWeek.Wednesday => "Ср",
        DayOfWeek.Thursday => "Чт",
        DayOfWeek.Friday => "Пт",
        DayOfWeek.Saturday => "Сб",
        DayOfWeek.Sunday => "Вс",
        _ => "?",
    };

    private static string ActionRu(ScheduleAction a) => a switch
    {
        ScheduleAction.StartMining => "включить",
        ScheduleAction.StopMining => "выключить",
        ScheduleAction.RestartMining => "рестарт",
        ScheduleAction.Reboot => "ребут",
        _ => "—",
    };

    private static bool TryParseHm(string? s, out int h, out int m)
    {
        h = m = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var parts = s.Split(':');
        if (parts.Length != 2) return false;
        return int.TryParse(parts[0], out h) && int.TryParse(parts[1], out m)
               && h is >= 0 and <= 23 && m is >= 0 and <= 59;
    }

    private static async Task<IResult> CreateAsync(
        ScheduleCreateRequest req, AsicFarmDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return Results.BadRequest(new { error = "name required" });

        if (req.TargetKind != ScheduleTargetKind.All && string.IsNullOrWhiteSpace(req.TargetId))
            return Results.BadRequest(new { error = "targetId required" });

        if (req.Actions is null || req.Actions.Count == 0)
            return Results.BadRequest(new { error = "at least one action required" });

        foreach (var a in req.Actions)
        {
            if (a.Action == ScheduleAction.None) continue;
            if (!TryParseHm(a.Time, out _, out _))
                return Results.BadRequest(new { error = $"invalid time for {a.Day}: '{a.Time}'" });
        }

        var tz = req.TimeZoneId;
        if (string.IsNullOrWhiteSpace(tz)) tz = "UTC";

        if (!TzUtil.TryResolve(tz, out _))
            return Results.BadRequest(new { error = $"unknown timezone '{tz}'" });

        var entity = new ScheduleEntity
        {
            Name = req.Name.Trim(),
            TargetKind = req.TargetKind.ToString(),
            TargetId = req.TargetId ?? "",
            Enabled = req.Enabled,
            TimeZoneId = tz,
            ActionsJson = JsonSerializer.Serialize(req.Actions, JsonOpts),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Schedules.Add(entity);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new { id = entity.Id });
    }

    private static async Task<IResult> DeleteAsync(long id, AsicFarmDbContext db, CancellationToken ct)
    {
        var s = await db.Schedules.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return Results.NotFound();
        db.Schedules.Remove(s);
        await db.SaveChangesAsync(ct);
        return Results.Ok();
    }

    private static async Task<IResult> EnableAsync(long id, AsicFarmDbContext db, CancellationToken ct)
    {
        var s = await db.Schedules.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return Results.NotFound();
        s.Enabled = true;
        await db.SaveChangesAsync(ct);
        return Results.Ok();
    }

    private static async Task<IResult> DisableAsync(long id, AsicFarmDbContext db, CancellationToken ct)
    {
        var s = await db.Schedules.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return Results.NotFound();
        s.Enabled = false;
        await db.SaveChangesAsync(ct);
        return Results.Ok();
    }

    private static async Task<IResult> RunsAsync(long id, AsicFarmDbContext db, CancellationToken ct)
    {
        var all = await db.ScheduleRuns
            .Where(r => r.ScheduleId == id)
            .Select(r => new ScheduleRunDto(
                r.Id, r.RunAt, r.Description, r.TotalTargets, r.Succeeded, r.Failed, r.Error))
            .ToListAsync(ct);

        var list = all
            .OrderByDescending(r => r.RunAt)
            .Take(50)
            .ToList();

        return Results.Ok(list);
    }
}