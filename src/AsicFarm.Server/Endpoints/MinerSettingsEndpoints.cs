using System.Text.Json;
using AsicFarm.Server.Data;
using AsicFarm.Server.Hubs;
using AsicFarm.Shared.Contracts;
using AsicFarm.Shared.Vnish;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AsicFarm.Server.Endpoints;

public static class MinerSettingsEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public static void MapMinerSettings(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/miners/{minerId}");
        g.MapPost("/settings/preset-switcher", SetPresetSwitcherAsync);
        g.MapGet("/presets", GetPresetsAsync);
        g.MapGet("/settings", GetSettingsAsync);
        g.MapGet("/raw-settings", GetRawSettingsAsync);
    }

    private static async Task<IResult> SetPresetSwitcherAsync(
        string minerId, PresetSwitcherRequest body, AsicFarmDbContext db, IHubContext<ControlHub> hub, ILogger<Program> log, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.MinPreset) || string.IsNullOrWhiteSpace(body.MaxPreset))
            return Results.BadRequest(new { error = "minPreset and maxPreset are required" });

        // Проверка на дефолтный пресет
        if (body.MinPreset.Equals("default", StringComparison.OrdinalIgnoreCase) ||
            body.MaxPreset.Equals("default", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new { error = "Preset switcher can not be enabled with default preset" });

        // VNISH: Температура понижения (decrease) должна быть выше температуры повышения (rise) минимум на 5°C
        if (body.DecreaseTemp < body.RiseTemp + 5)
            return Results.BadRequest(new { error = "decreaseTemp must be at least 5 degrees higher than riseTemp" });

        var cmd = new MinerCommand(Guid.NewGuid().ToString("N"), minerId, MinerCommandKind.ConfigurePresetSwitcher,
            Enabled: body.Enabled, MinPreset: body.MinPreset, MaxPreset: body.MaxPreset, RiseTemp: body.RiseTemp, DecreaseTemp: body.DecreaseTemp);

        var (result, error) = await DispatchAsync(db, hub, cmd, log, ct);
        if (error is not null) return error;

        return result!.Success ? Results.Ok() : Results.Json(new { error = result.Error }, statusCode: 500);
    }

    private static async Task<IResult> GetPresetsAsync(
        string minerId, AsicFarmDbContext db, IHubContext<ControlHub> hub, ILogger<Program> log, CancellationToken ct)
    {
        var cmd = new MinerCommand(Guid.NewGuid().ToString("N"), minerId, MinerCommandKind.GetAutotunePresets);
        var (result, error) = await DispatchAsync(db, hub, cmd, log, ct);

        if (error is not null) return error;
        if (!result!.Success) return Results.Json(new { error = result.Error }, statusCode: 500);
        if (string.IsNullOrEmpty(result.PayloadJson)) return Results.Ok(new List<VnishAutotunePreset>());

        try
        {
            var presets = JsonSerializer.Deserialize<List<VnishAutotunePreset>>(result.PayloadJson, JsonOpts) ?? new List<VnishAutotunePreset>();
            return Results.Ok(presets);
        }
        catch (JsonException ex)
        {
            log.LogError(ex, "Invalid presets payload for {Miner}", minerId);
            return Results.Json(new { error = "Invalid presets payload: " + ex.Message }, statusCode: 500);
        }
    }

    private static async Task<IResult> GetSettingsAsync(
        string minerId, AsicFarmDbContext db, IHubContext<ControlHub> hub, ILogger<Program> log, CancellationToken ct)
    {
        var cmd = new MinerCommand(Guid.NewGuid().ToString("N"), minerId, MinerCommandKind.GetSettings);
        var (result, error) = await DispatchAsync(db, hub, cmd, log, ct);

        if (error is not null) return error;
        if (!result!.Success) return Results.Json(new { error = result.Error }, statusCode: 500);
        if (string.IsNullOrEmpty(result.PayloadJson)) return Results.NotFound();

        try
        {
            var settings = JsonSerializer.Deserialize<MinerSettingsDto>(result.PayloadJson, JsonOpts);
            return Results.Ok(settings);
        }
        catch (JsonException ex)
        {
            return Results.Json(new { error = "Invalid settings payload: " + ex.Message }, statusCode: 500);
        }
    }

    private static async Task<IResult> GetRawSettingsAsync(
        string minerId, AsicFarmDbContext db, IHubContext<ControlHub> hub, ILogger<Program> log, CancellationToken ct)
    {
        var cmd = new MinerCommand(Guid.NewGuid().ToString("N"), minerId, MinerCommandKind.GetRawSettings);
        var (result, error) = await DispatchAsync(db, hub, cmd, log, ct);

        if (error is not null) return error;
        if (!result!.Success) return Results.Json(new { error = result.Error }, statusCode: 500);
        if (string.IsNullOrEmpty(result.PayloadJson)) return Results.NotFound();

        return Results.Content(result.PayloadJson, "application/json");
    }

    private static async Task<(MinerCommandResult? Result, IResult? Error)> DispatchAsync(
        AsicFarmDbContext db, IHubContext<ControlHub> hub, MinerCommand cmd, ILogger log, CancellationToken ct)
    {
        var miner = await db.Miners.FirstOrDefaultAsync(m => m.Id == cmd.MinerId, ct);
        if (miner is null) return (null, Results.NotFound(new { error = $"Miner '{cmd.MinerId}' not found" }));
        if (string.IsNullOrWhiteSpace(miner.WatchdogId)) return (null, Results.BadRequest(new { error = "Miner has no watchdog bound" }));

        log.LogInformation("Dispatch {Kind} → {Miner} via {Watchdog} (req={Req})", cmd.Kind, cmd.MinerId, miner.WatchdogId, cmd.RequestId);
        var result = await ControlHub.DispatchAsync(hub, miner.WatchdogId, cmd, TimeSpan.FromSeconds(60), ct);
        return (result, null);
    }
}

public sealed record PresetSwitcherRequest(bool Enabled, string MinPreset, string MaxPreset, int RiseTemp, int DecreaseTemp);