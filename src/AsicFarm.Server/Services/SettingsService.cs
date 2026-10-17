using System.Collections.Concurrent;
using AsicFarm.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace AsicFarm.Server.Services;

/// Настройки с override из БД поверх appsettings.json.
/// Кэш на 15 секунд, чтобы не бить БД на каждый чих.
public sealed class SettingsService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<SettingsService> _log;

    private readonly ConcurrentDictionary<string, (string? Value, DateTimeOffset At)> _cache = new();

    public SettingsService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<SettingsService> log)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _log = log;
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(key, out var cached) &&
            DateTimeOffset.UtcNow - cached.At < CacheTtl)
            return cached.Value;

        string? value = null;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AsicFarmDbContext>();
            var row = await db.AppSettings.FirstOrDefaultAsync(x => x.Key == key, ct);
            value = row?.Value;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "AppSettings read failed for {Key}", key);
        }

        if (string.IsNullOrEmpty(value))
            value = _config[key];

        _cache[key] = (value, DateTimeOffset.UtcNow);
        return value;
    }

    public async Task SetAsync(string key, string? value, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AsicFarmDbContext>();

        var row = await db.AppSettings.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (row is null)
        {
            row = new AppSettingEntity { Key = key };
            db.AppSettings.Add(row);
        }
        row.Value = value ?? "";
        row.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        _cache[key] = (value, DateTimeOffset.UtcNow);
    }
}