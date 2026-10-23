using AsicFarm.Server.Auth;
using AsicFarm.Server.Data;
using AsicFarm.Server.Endpoints;
using AsicFarm.Server.Hubs;
using AsicFarm.Server.Services;
using AsicFarm.Shared.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;

var baseDir = AppContext.BaseDirectory;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(path: Path.Combine(baseDir, "logs", "server-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14, outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("AsicFarm.Server starting... (base dir: {BaseDir})", baseDir);

    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = baseDir });
    builder.Host.UseSerilog();

    builder.Services.Configure<ServerOptions>(builder.Configuration.GetSection(ServerOptions.SectionName));

    var dbPath = builder.Configuration.GetValue<string>("Server:DatabasePath") ?? "asicfarm.db";
    if (!Path.IsPathRooted(dbPath)) dbPath = Path.Combine(baseDir, dbPath);

    builder.Services.AddDbContext<AsicFarmDbContext>(opt => opt.UseSqlite($"Data Source={dbPath}"));

    builder.Services.AddSignalR(options =>
    {
        options.MaximumReceiveMessageSize = 10 * 1024 * 1024;
        options.ClientTimeoutInterval = TimeSpan.FromMinutes(5);
        options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        options.HandshakeTimeout = TimeSpan.FromSeconds(30);
    });

    builder.Services.AddSingleton<SettingsService>();
    builder.Services.AddSingleton<TelegramNotifier>();
    builder.Services.AddSingleton<DetailedSnapshotBroker>();
    builder.Services.AddHostedService<ScheduleRunner>();
    builder.Services.AddHostedService<AlertRunner>();
    builder.Services.AddHostedService<AutoActionRunner>();
    builder.Services.AddHostedService<StaleCleanupService>();

    var app = builder.Build();

    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AsicFarmDbContext>();
        if (app.Environment.IsDevelopment())
        {
            Log.Information("Development mode — re-creating database schema");
            db.Database.EnsureDeleted();
        }
        db.Database.EnsureCreated();
        Log.Information("Database ready: {DbPath}", dbPath);
    }

    app.UseMiddleware<ApiKeyMiddleware>();

    app.MapMiners();
    app.MapIngest();
    app.MapCommands();
    app.MapSchedules();
    app.MapAlerts();
    app.MapSettings();
    app.MapAutoActions();

    // ПУЛЫ
    app.MapPost("/api/v1/miners/{minerId}/pools", async (
        string minerId, SetPoolsRequest request, AsicFarmDbContext db, IHubContext<ControlHub> hub, ILogger<Program> log) =>
    {
        try
        {
            var miner = await db.Miners.FirstOrDefaultAsync(m => m.Id == minerId);
            if (miner == null || string.IsNullOrEmpty(miner.WatchdogId)) return Results.NotFound();
            await hub.Clients.Group(ControlHub.GroupName(miner.WatchdogId)).SendAsync("SetPools", minerId, request.Pools);
            return Results.Ok();
        }
        catch (Exception ex) { return Results.Problem(ex.Message); }
    });

    // НАСТРОЙКИ
    app.MapPost("/api/v1/miners/{minerId}/settings/target-temp", async (
        string minerId, SetTargetTempRequest req, AsicFarmDbContext db, IHubContext<ControlHub> hub) =>
    {
        var miner = await db.Miners.FirstOrDefaultAsync(m => m.Id == minerId);
        if (miner == null || string.IsNullOrEmpty(miner.WatchdogId)) return Results.NotFound();
        var cmd = new MinerCommand(Guid.NewGuid().ToString("N"), minerId, MinerCommandKind.SetTargetTemp) { TargetTemp = req.TargetTemp };
        await hub.Clients.Group(ControlHub.GroupName(miner.WatchdogId)).SendAsync("ExecuteCommand", new ExecuteCommandMessage(cmd));
        return Results.Ok();
    });

    app.MapPost("/api/v1/miners/{minerId}/settings/cooling-mode", async (
        string minerId, SetCoolingModeRequest req, AsicFarmDbContext db, IHubContext<ControlHub> hub) =>
    {
        var miner = await db.Miners.FirstOrDefaultAsync(m => m.Id == minerId);
        if (miner == null || string.IsNullOrEmpty(miner.WatchdogId)) return Results.NotFound();
        var cmd = new MinerCommand(Guid.NewGuid().ToString("N"), minerId, MinerCommandKind.SetCoolingMode) { CoolingMode = req.Mode, FanDuty = req.FanDuty };
        await hub.Clients.Group(ControlHub.GroupName(miner.WatchdogId)).SendAsync("ExecuteCommand", new ExecuteCommandMessage(cmd));
        return Results.Ok();
    });

    app.MapPost("/api/v1/miners/{minerId}/settings/quiet-mode", async (
        string minerId, SetQuietModeRequest req, AsicFarmDbContext db, IHubContext<ControlHub> hub) =>
    {
        var miner = await db.Miners.FirstOrDefaultAsync(m => m.Id == minerId);
        if (miner == null || string.IsNullOrEmpty(miner.WatchdogId)) return Results.NotFound();
        var cmd = new MinerCommand(Guid.NewGuid().ToString("N"), minerId, MinerCommandKind.SetQuietMode) { Enabled = req.Enabled };
        await hub.Clients.Group(ControlHub.GroupName(miner.WatchdogId)).SendAsync("ExecuteCommand", new ExecuteCommandMessage(cmd));
        return Results.Ok();
    });

    app.MapPost("/api/v1/miners/{minerId}/settings/restart-temp", async (
        string minerId, SetRestartTempRequest req, AsicFarmDbContext db, IHubContext<ControlHub> hub) =>
    {
        var miner = await db.Miners.FirstOrDefaultAsync(m => m.Id == minerId);
        if (miner == null || string.IsNullOrEmpty(miner.WatchdogId)) return Results.NotFound();
        var cmd = new MinerCommand(Guid.NewGuid().ToString("N"), minerId, MinerCommandKind.SetRestartTemp) { Value = req.Temp };
        await hub.Clients.Group(ControlHub.GroupName(miner.WatchdogId)).SendAsync("ExecuteCommand", new ExecuteCommandMessage(cmd));
        return Results.Ok();
    });

    app.MapPost("/api/v1/miners/{minerId}/settings/startup-delay", async (
        string minerId, SetStartupDelayRequest req, AsicFarmDbContext db, IHubContext<ControlHub> hub) =>
    {
        var miner = await db.Miners.FirstOrDefaultAsync(m => m.Id == minerId);
        if (miner == null || string.IsNullOrEmpty(miner.WatchdogId)) return Results.NotFound();
        var cmd = new MinerCommand(Guid.NewGuid().ToString("N"), minerId, MinerCommandKind.SetStartupDelay) { Value = req.Seconds };
        await hub.Clients.Group(ControlHub.GroupName(miner.WatchdogId)).SendAsync("ExecuteCommand", new ExecuteCommandMessage(cmd));
        return Results.Ok();
    });

    app.MapHub<TelemetryHub>(TelemetryHub.Route);
    app.MapHub<ControlHub>(ControlHub.Route);

    Log.Information("Server started, listening on http://0.0.0.0:5000");
    await app.RunAsync();
}
catch (Exception ex) { Log.Fatal(ex, "Server terminated unexpectedly"); }
finally { await Log.CloseAndFlushAsync(); }

public record SetPoolsRequest(List<PoolConfigDto> Pools);