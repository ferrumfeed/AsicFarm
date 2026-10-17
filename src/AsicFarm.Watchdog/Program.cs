using AsicFarm.Watchdog;
using AsicFarm.Watchdog.Services;
using Serilog;
using Serilog.Events;

var baseDir = AppContext.BaseDirectory;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: Path.Combine(baseDir, "logs", "watchdog-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("AsicFarm.Watchdog starting... (base dir: {BaseDir})", baseDir);

    var builder = Host.CreateApplicationBuilder(args);

    builder.Configuration
        .SetBasePath(baseDir)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

    builder.Services.AddWindowsService(o => o.ServiceName = "AsicFarm.Watchdog");

    builder.Logging.ClearProviders();
    builder.Logging.AddSerilog(Log.Logger, dispose: false);

    builder.Services.Configure<WatchdogOptions>(
        builder.Configuration.GetSection(WatchdogOptions.SectionName));

    builder.Services.AddSingleton<MinerRegistry>();
    builder.Services.AddSingleton<MinerHostRegistry>();

    builder.Services.AddHttpClient<ServerClient>();

    builder.Services.AddHostedService<SubnetScanner>();
    builder.Services.AddHostedService<MinerPoller>();
    builder.Services.AddHostedService<ControlChannel>();

    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}