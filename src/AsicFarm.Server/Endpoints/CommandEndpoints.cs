using AsicFarm.Server.Data;
using AsicFarm.Server.Hubs;
using AsicFarm.Shared.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AsicFarm.Server.Endpoints;

public static class CommandEndpoints
{
    public static void MapCommands(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/commands", HandleCommandAsync);
        app.MapGet("/api/v1/watchdogs/online", (IHubContext<ControlHub> _) =>
            Results.Ok(ControlHub.OnlineWatchdogs));
    }

    private static async Task<IResult> HandleCommandAsync(
        MinerCommand cmd,
        AsicFarmDbContext db,
        IHubContext<ControlHub> hub,
        ILogger<Program> log,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.MinerId))
            return Results.BadRequest(new { error = "minerId required" });

        var miner = await db.Miners.FirstOrDefaultAsync(m => m.Id == cmd.MinerId, ct);
        if (miner is null)
            return Results.NotFound(new { error = $"Miner '{cmd.MinerId}' not found" });

        var wdId = miner.WatchdogId;
        if (string.IsNullOrWhiteSpace(wdId))
            return Results.BadRequest(new { error = "Miner has no watchdog bound" });

        // Заполняем RequestId, если пусто
        var requestId = string.IsNullOrWhiteSpace(cmd.RequestId)
            ? Guid.NewGuid().ToString("N")
            : cmd.RequestId;
        var fullCmd = cmd with { RequestId = requestId };

        log.LogInformation("Dispatch {Kind} → {Miner} via {Watchdog} (req={Req})",
            fullCmd.Kind, cmd.MinerId, wdId, requestId);

        var result = await ControlHub.DispatchAsync(
            hub, wdId, fullCmd, TimeSpan.FromSeconds(60), ct);

        log.LogInformation("Command {Req} result: success={Success} err={Err}",
            requestId, result.Success, result.Error);

        return result.Success
            ? Results.Ok(result)
            : Results.Json(result, statusCode: 500);
    }
}