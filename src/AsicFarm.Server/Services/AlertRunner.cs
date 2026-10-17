using System.Collections.Concurrent;
using System.Text.Json;
using AsicFarm.Server.Data;
using AsicFarm.Shared.Domain;
using Microsoft.EntityFrameworkCore;

namespace AsicFarm.Server.Services;

public sealed class AlertRunner : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TelegramNotifier _telegram;
    private readonly ILogger<AlertRunner> _log;

    private readonly ConcurrentDictionary<string, DateTimeOffset> _activeSince = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastFired = new();

    public AlertRunner(
        IServiceScopeFactory scopeFactory,
        TelegramNotifier telegram,
        ILogger<AlertRunner> log)
    {
        _scopeFactory = scopeFactory;
        _telegram = telegram;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = await _telegram.IsEnabledAsync(stoppingToken);
        _log.LogInformation("AlertRunner started, check every 30s. Telegram enabled: {Enabled}",
            enabled);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await CheckAsync(stoppingToken); }
            catch (Exception ex) { _log.LogError(ex, "AlertRunner tick failed"); }

            try { await timer.WaitForNextTickAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task CheckAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AsicFarmDbContext>();

        var rules = await db.AlertRules.Where(r => r.Enabled).ToListAsync(ct);
        if (rules.Count == 0) return;

        var miners = await db.Miners.ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;

        foreach (var rule in rules)
        {
            var targets = ResolveTargets(miners, rule.Target);

            foreach (var miner in targets)
            {
                var snap = TryDeserialize(miner.LastSnapshotJson);
                var (conditionMet, message) = Evaluate(rule, miner, snap, now);

                var key = $"{rule.Id}:{miner.Id}";

                if (conditionMet)
                {
                    if (!_activeSince.TryGetValue(key, out var since))
                    {
                        _activeSince[key] = now;
                        continue;
                    }

                    if ((now - since).TotalSeconds < rule.ForSeconds) continue;

                    if (_lastFired.TryGetValue(key, out var last) &&
                        (now - last).TotalSeconds < rule.CooldownSeconds)
                        continue;

                    _lastFired[key] = now;
                    await FireAsync(db, rule, miner, message, ct);
                }
                else
                {
                    _activeSince.TryRemove(key, out _);
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static List<MinerEntity> ResolveTargets(List<MinerEntity> all, string target)
    {
        if (string.IsNullOrWhiteSpace(target) || target == "All") return all;
        if (target.StartsWith("Miner:", StringComparison.Ordinal))
        {
            var id = target["Miner:".Length..];
            return all.Where(m => m.Id == id).ToList();
        }
        if (target.StartsWith("Watchdog:", StringComparison.Ordinal))
        {
            var id = target["Watchdog:".Length..];
            return all.Where(m => m.WatchdogId == id).ToList();
        }
        return all;
    }

    private static (bool ConditionMet, string Message) Evaluate(
        AlertRuleEntity rule, MinerEntity miner, MinerSnapshot? snap, DateTimeOffset now)
    {
        switch (rule.Type)
        {
            case "MinerOffline":
                {
                    var age = now - miner.LastSeen;
                    if (age.TotalSeconds > rule.ForSeconds)
                        return (true, $"🔴 <b>{miner.Model}</b> ({miner.Ip}) не отвечает {Human(age)}.");
                    return (false, "");
                }

            case "HashrateBelow":
                {
                    if (snap is null || snap.HashrateNominalGhs <= 0) return (false, "");
                    var pct = snap.HashrateRealtimeGhs / snap.HashrateNominalGhs * 100.0;
                    if (pct < rule.Threshold)
                        return (true, $"⚠️ <b>{miner.Model}</b> ({miner.Ip}): хешрейт {pct:F1}% (порог {rule.Threshold:F0}%).");
                    return (false, "");
                }

            case "TempAbove":
                {
                    if (snap is null) return (false, "");
                    var maxTemp = snap.ChipTemp?.Max ?? 0;
                    if (maxTemp > rule.Threshold)
                        return (true, $"🔥 <b>{miner.Model}</b> ({miner.Ip}): температура чипа {maxTemp}°C (порог {rule.Threshold:F0}°C).");
                    return (false, "");
                }

            case "StateIs":
                {
                    if (snap is null || string.IsNullOrWhiteSpace(rule.Extra)) return (false, "");
                    if (string.Equals(snap.State.ToString(), rule.Extra, StringComparison.OrdinalIgnoreCase))
                        return (true, $"ℹ️ <b>{miner.Model}</b> ({miner.Ip}): состояние <b>{snap.State}</b>.");
                    return (false, "");
                }

            default:
                return (false, "");
        }
    }

    private async Task FireAsync(
        AsicFarmDbContext db,
        AlertRuleEntity rule,
        MinerEntity miner,
        string message,
        CancellationToken ct)
    {
        _log.LogInformation("ALERT rule={Rule} miner={Miner} msg={Msg}",
            rule.Name, miner.Id, message);

        var evt = new AlertEventEntity
        {
            RuleId = rule.Id,
            RuleName = rule.Name,
            MinerId = miner.Id,
            MinerIp = miner.Ip,
            FiredAt = DateTimeOffset.UtcNow,
            Message = message,
            Delivered = false,
        };

        if (rule.Channel == "Telegram")
        {
            var (ok, err) = await _telegram.SendAsync(rule.ChannelConfig, message, ct);
            evt.Delivered = ok;
            evt.DeliveryError = err;
        }
        else
        {
            evt.DeliveryError = $"Unknown channel '{rule.Channel}'";
        }

        db.AlertEvents.Add(evt);
    }

    private static string Human(TimeSpan t)
    {
        if (t.TotalMinutes < 60) return $"{(int)t.TotalMinutes} мин";
        if (t.TotalHours < 24) return $"{(int)t.TotalHours} ч";
        return $"{(int)t.TotalDays} д";
    }

    private static MinerSnapshot? TryDeserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<MinerSnapshot>(json,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch { return null; }
    }
}