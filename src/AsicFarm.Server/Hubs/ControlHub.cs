using System.Collections.Concurrent;
using AsicFarm.Shared.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace AsicFarm.Server.Hubs;

public sealed class ControlHub : Hub
{
    public const string Route = "/hubs/control";

    private static readonly ConcurrentDictionary<string, string> _onlineWatchdogs = new();
    private static readonly ConcurrentDictionary<string, TaskCompletionSource<MinerCommandResult>> _pending = new();

    public static IReadOnlyCollection<string> OnlineWatchdogs => _onlineWatchdogs.Keys.ToArray();

    public override async Task OnConnectedAsync()
    {
        var wdId = Context.GetHttpContext()?.Request.Query["watchdogId"].ToString();
        if (!string.IsNullOrWhiteSpace(wdId))
        {
            _onlineWatchdogs[wdId] = Context.ConnectionId;
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(wdId));
            await Clients.All.SendAsync("watchdog.online", wdId);
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var pair = _onlineWatchdogs.FirstOrDefault(kv => kv.Value == Context.ConnectionId);
        if (!string.IsNullOrEmpty(pair.Key))
        {
            _onlineWatchdogs.TryRemove(pair.Key, out _);
            await Clients.All.SendAsync("watchdog.offline", pair.Key);
        }
        await base.OnDisconnectedAsync(exception);
    }

    public Task ReportResult(CommandResultMessage msg)
    {
        if (_pending.TryRemove(msg.Result.RequestId, out var tcs))
            tcs.TrySetResult(msg.Result);
        return Task.CompletedTask;
    }

    public static async Task<MinerCommandResult> DispatchAsync(
        IHubContext<ControlHub> hub,
        string watchdogId,
        MinerCommand command,
        TimeSpan timeout,
        CancellationToken ct)
    {
        if (!_onlineWatchdogs.ContainsKey(watchdogId))
        {
            return new MinerCommandResult(
                command.RequestId, command.MinerId, false,
                $"Watchdog '{watchdogId}' не в сети", DateTimeOffset.UtcNow);
        }

        var tcs = new TaskCompletionSource<MinerCommandResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[command.RequestId] = tcs;

        try
        {
            await hub.Clients.Group(GroupName(watchdogId))
                             .SendAsync("ExecuteCommand", new ExecuteCommandMessage(command), ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, timeoutCts.Token));
            if (completed == tcs.Task)
                return await tcs.Task;

            _pending.TryRemove(command.RequestId, out _);
            return new MinerCommandResult(
                command.RequestId, command.MinerId, false,
                "Timeout ожидания результата", DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            _pending.TryRemove(command.RequestId, out _);
            return new MinerCommandResult(
                command.RequestId, command.MinerId, false, ex.Message, DateTimeOffset.UtcNow);
        }
    }

    // ИСПРАВЛЕНО: было private, стало public
    public static string GroupName(string watchdogId) => $"watchdog:{watchdogId}";
}