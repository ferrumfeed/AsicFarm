using AsicFarm.Server.Auth;
using AsicFarm.Server.Data;
using AsicFarm.Server.Endpoints;
using AsicFarm.Server.Hubs;
using AsicFarm.Server.Services;
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
    .WriteTo.File(
        path: Path.Combine(baseDir, "logs", "server-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("AsicFarm.Server starting... (base dir: {BaseDir})", baseDir);

    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = args,
        ContentRootPath = baseDir,
    });

    builder.Host.UseSerilog();

    builder.Services.Configure<ServerOptions>(
        builder.Configuration.GetSection(ServerOptions.SectionName));

    var dbPath = builder.Configuration.GetValue<string>("Server:DatabasePath") ?? "asicfarm.db";
    if (!Path.IsPathRooted(dbPath))
        dbPath = Path.Combine(baseDir, dbPath);

    builder.Services.AddDbContext<AsicFarmDbContext>(opt =>
        opt.UseSqlite($"Data Source={dbPath}"));

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
    app.MapHub<TelemetryHub>(TelemetryHub.Route);
    app.MapHub<ControlHub>(ControlHub.Route);

    Log.Information("Server started, listening on http://0.0.0.0:5000");
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Server terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}