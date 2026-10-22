using AsicFarm.Server.Auth;
using AsicFarm.Server.Data;
using AsicFarm.Server.Endpoints;
using AsicFarm.Server.Hubs;
using AsicFarm.Server.Services;
using AsicFarm.Shared.Contracts;
using AsicFarm.Shared.Vnish;
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
    builder.Services.AddSingleton<PresetBroker>(); // Брокер для пресетов
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

    // ================= ПУЛЫ =================
    app.MapPost("/api/v1/miners/{minerId}/pools", async (
        string minerId, SetPoolsRequest request, AsicFarmDbContext db, IHubContext<ControlHub> hub, ILogger<Program> log) =>
    {
        try
        {
            log.LogInformation("POST /pools: miner={MinerId}, pools={Count}", minerId, request.Pools.Count);
            var miner = await db.Miners.FirstOrDefaultAsync(m => m.Id == minerId);
            if (miner == null || string.IsNullOrEmpty(miner.WatchdogId)) return Results.NotFound();
            var groupName = ControlHub.GroupName(miner.WatchdogId);
            await hub.Clients.Group(groupName).SendAsync("SetPools", minerId, request.Pools);
            log.LogInformation("POST /pools: sent to group '{GroupName}'", groupName);
            return Results.Ok();
        }
        catch (Exception ex) { log.LogError(ex, "POST /pools: FAILED"); return Results.Problem(ex.Message); }
    });

    // ================= НАСТРОЙКИ =================
    app.MapPost("/api/v1/miners/{minerId}/settings/target-temp", async (
        string minerId, SetTargetTempRequest req, AsicFarmDbContext db, IHubContext<ControlHub> hub, ILogger<Program> log) =>
    {
        log.LogInformation("Endpoint HIT: target-temp miner={MinerId}, temp={Temp}", minerId, req.TargetTemp);
        var miner = await db.Miners.FirstOrDefaultAsync(m => m.Id == minerId);
        if (miner == null || string.IsNullOrEmpty(miner.WatchdogId)) return Results.NotFound();
        var cmd = new MinerCommand(Guid.NewGuid().ToString("N"), minerId, MinerCommandKind.SetTargetTemp) { TargetTemp = req.TargetTemp };
        await hub.Clients.Group(ControlHub.GroupName(miner.WatchdogId)).SendAsync("ExecuteCommand", new ExecuteCommandMessage(cmd));
        return Results.Ok();
    });

    app.MapPost("/api/v1/miners/{minerId}/settings/preset-switcher", async (
        string minerId, ConfigurePresetSwitcherRequest req, AsicFarmDbContext db, IHubContext<ControlHub> hub, ILogger<Program> log) =>
    {
        log.LogInformation("Endpoint HIT: preset-switcher miner={MinerId}", minerId);
        var miner = await db.Miners.FirstOrDefaultAsync(m => m.Id == minerId);
        if (miner == null || string.IsNullOrEmpty(miner.WatchdogId)) return Results.NotFound();
        var cmd = new MinerCommand(Guid.NewGuid().ToString("N"), minerId, MinerCommandKind.ConfigurePresetSwitcher) { PresetSwitcher = req };
        await hub.Clients.Group(ControlHub.GroupName(miner.WatchdogId)).SendAsync("ExecuteCommand", new ExecuteCommandMessage(cmd));
        return Results.Ok();
    });

    // ================= ПРЕСЕТЫ (с ожиданием ответа) =================
    app.MapGet("/api/v1/miners/{minerId}/presets", async (
        string minerId, AsicFarmDbContext db, IHubContext<ControlHub> hub, PresetBroker broker, ILogger<Program> log) =>
    {
        var miner = await db.Miners.FirstOrDefaultAsync(m => m.Id == minerId);
        if (miner == null || string.IsNullOrEmpty(miner.WatchdogId)) return Results.NotFound();

        var requestId = Guid.NewGuid().ToString("N");
        var tcs = broker.Register(requestId);

        var cmd = new MinerCommand(requestId, minerId, MinerCommandKind.GetPresets);
        await hub.Clients.Group(ControlHub.GroupName(miner.WatchdogId)).SendAsync("ExecuteCommand", new ExecuteCommandMessage(cmd));

        // Увеличиваем таймаут до 90 секунд, потому что ответ от VNISH очень большой
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, timeoutCts.Token));

        if (completed == tcs.Task)
        {
            var presets = await tcs.Task;
            log.LogInformation("Presets for {MinerId}: {Count} items", minerId, presets.Count);
            return Results.Ok(presets);
        }

        broker.Remove(requestId);
        log.LogWarning("Timeout waiting for presets from {MinerId}", minerId);
        return Results.Json(new { error = "Timeout waiting for presets" }, statusCode: 504);
    });

    // ================= ПРИЕМ ПРЕСЕТОВ ОТ ВАТЧДОГА =================
    app.MapPost("/api/v1/ingest/presets", (PresetsResponse response, PresetBroker broker) =>
    {
        broker.Complete(response.RequestId, response.Presets);
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