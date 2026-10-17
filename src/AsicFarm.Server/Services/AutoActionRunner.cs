using System.Collections.Concurrent;
using System.Text.Json;
using AsicFarm.Server.Data;
using AsicFarm.Server.Hubs;
using AsicFarm.Shared.Contracts;
using AsicFarm.Shared.Domain;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AsicFarm.Server.Services;

public sealed class AutoActionRunner : BackgroundService
{
    private const int MaxActionsPerMinute = 5;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<ControlHub> _hub;
    private readonly ILogger<AutoActionRunner> _log;

    /// "ruleId:minerId" → когда условие начало выполняться.
    private readonly ConcurrentDictionary<string, DateTimeOffset> _activeSince = new();

    /// minerId → когда последний раз делали действие (для cooldown).
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastActionPerMiner = new();

    /// Timestamps последних действий — для глобального rate-limit.
    private readonly ConcurrentQueue<DateTimeOffset> _globalActions = new();

    public AutoActionRunner(
        IServiceScopeFactory scopeFactory,
        IHubContext<ControlHub> hub,
        ILogger<AutoActionRunner> log)
    {
        _scopeFactory = scopeFactory;
        _hub = hub;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("AutoActionRunner started, check every 30s");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await CheckAsync(stoppingToken); }
            catch (Exception ex) { _log.LogError(ex, "AutoActionRunner tick failed"); }

            try { await timer.WaitForNextTickAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task CheckAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AsicFarmDbContext>();

        var rules = await db.AutoActions.Where(r => r.Enabled).ToListAsync(ct);
        if (rules.Count == 0) return;

        var miners = await db.Miners.ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;

        foreach (var rule in rules)
        {
            var targets = ResolveTargets(miners, rule.Target);

            foreach (var miner in targets)
            {
                var snap = TryDeserialize(miner.LastSnapshotJson);
                var (conditionMet, reason) = Evaluate(rule, miner, snap, now);

                var key = $"{rule.Id}:{miner.Id}";

                if (!conditionMet)
                {
                    _activeSince.TryRemove(key, out _);
                    continue;
                }

                if (!_activeSince.TryGetValue(key, out var since))
                {
                    _activeSince[key] = now;
                    continue;
                }

                if ((now - since).TotalSeconds < rule.ForSeconds) continue;

                // Cooldown на майнер
                if (_lastActionPerMiner.TryGetValue(miner.Id, out var lastAct) &&
                    (now - lastAct).TotalSeconds < rule.CooldownSeconds)
                    continue;

                // Global rate-limit
                if (!TryReserveGlobalSlot(now))
                {
                    _log.LogWarning("AutoAction: глобальный rate-limit, пропускаю {Rule} для {Miner}",
                        rule.Name, miner.Id);
                    continue;
                }

                _lastActionPerMiner[miner.Id] = now;
                _activeSince.TryRemove(key, out _);

                await FireAsync(db, rule, miner, reason, ct);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private bool TryReserveGlobalSlot(DateTimeOffset now)
    {
        while (_globalActions.TryPeek(out var oldest) &&
               (now - oldest).TotalSeconds > 60)
        {
            _globalActions.TryDequeue(out _);
        }

        if (_globalActions.Count >= MaxActionsPerMinute) return false;

        _globalActions.Enqueue(now);
        return true;
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

    private static (bool ConditionMet, string Reason) Evaluate(
        AutoActionEntity rule, MinerEntity miner, MinerSnapshot? snap, DateTimeOffset now)
    {
        switch (rule.Trigger)
        {
            case "HashrateBelow":
                {
                    if (snap is null) return (false, "");
                    if (snap.State != MinerState.Mining) return (false, "");

                    var nominal = snap.HashrateNominalGhs;
                    if (nominal <= 0) return (false, "");

                    var pct = snap.HashrateRealtimeGhs / nominal * 100.0;
                    if (pct < rule.Threshold)
                        return (true, $"хешрейт {pct:F1}% < {rule.Threshold:F0}%");
                    return (false, "");
                }

            case "TempAbove":
                {
                    if (snap is null) return (false, "");
                    var maxTemp = snap.ChipTemp?.Max ?? 0;
                    if (maxTemp > rule.Threshold)
                        return (true, $"температура {maxTemp}°C > {rule.Threshold:F0}°C");
                    return (false, "");
                }

            case "Offline":
                {
                    var age = now - miner.LastSeen;
                    if (age.TotalSeconds > rule.ForSeconds)
                        return (true, $"офлайн {age.TotalMinutes:F0} мин");
                    return (false, "");
                }

            default:
                return (false, "");
        }
    }

    private async Task FireAsync(
        AsicFarmDbContext db,
        AutoActionEntity rule,
        MinerEntity miner,
        string reason,
        CancellationToken ct)
    {
        var kind = rule.Action switch
        {
            "Reboot" => MinerCommandKind.Reboot,
            "RestartMining" => MinerCommandKind.RestartMining,
            "StopMining" => MinerCommandKind.StopMining,
            "PauseMining" => MinerCommandKind.PauseMining,
            _ => (MinerCommandKind?)null,
        };

        if (kind is null)
        {
            _log.LogWarning("AutoAction rule {Rule}: unknown action '{Action}'", rule.Name, rule.Action);
            return;
        }

        // Офлайн-майнеру команду не отправить
        if (rule.Trigger == "Offline")
        {
            _log.LogInformation("AutoAction: {Miner} офлайн, действие {Action} не отправлено",
                miner.Id, rule.Action);
            db.AutoActionEvents.Add(new AutoActionEventEntity
            {
                RuleId = rule.Id,
                RuleName = rule.Name,
                MinerId = miner.Id,
                MinerIp = miner.Ip,
                Action = rule.Action,
                FiredAt = DateTimeOffset.UtcNow,
                Success = false,
                Error = "Miner offline, command skipped",
            });
            rule.LastFiredAt = DateTimeOffset.UtcNow;
            return;
        }

        _log.LogInformation("AutoAction: {Rule} → {Action} на {Miner} ({Reason})",
            rule.Name, rule.Action, miner.Id, reason);

        var cmd = new MinerCommand(
            RequestId: Guid.NewGuid().ToString("N"),
            MinerId: miner.Id,
            Kind: kind.Value);

        var (ok, error) = (false, (string?)null);
        try
        {
            var r = await ControlHub.DispatchAsync(
                _hub, miner.WatchdogId, cmd, TimeSpan.FromSeconds(30), ct);
            ok = r.Success;
            error = r.Error;
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        db.AutoActionEvents.Add(new AutoActionEventEntity
        {
            RuleId = rule.Id,
            RuleName = rule.Name,
            MinerId = miner.Id,
            MinerIp = miner.Ip,
            Action = rule.Action,
            FiredAt = DateTimeOffset.UtcNow,
            Success = ok,
            Error = error,
        });

        rule.LastFiredAt = DateTimeOffset.UtcNow;
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