using System.Collections.Concurrent;
using AsicFarm.Shared.Domain;

namespace AsicFarm.Server.Services;

/// Держит "ожидающие" запросы детального снапшота.
/// Клиент вызывает GET /detailed → сервер регистрирует TaskCompletionSource и ждёт.
/// Ватчдог присылает снапшот POST'ом на /api/v1/ingest/detailed → сервер резолвит TCS.
public sealed class DetailedSnapshotBroker
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<MinerDetailedSnapshot>> _pending = new();

    public TaskCompletionSource<MinerDetailedSnapshot> Register(string requestId)
    {
        var tcs = new TaskCompletionSource<MinerDetailedSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = tcs;
        return tcs;
    }

    public void Complete(string requestId, MinerDetailedSnapshot snapshot)
    {
        if (_pending.TryRemove(requestId, out var tcs))
            tcs.TrySetResult(snapshot);
    }

    public void Remove(string requestId)
    {
        _pending.TryRemove(requestId, out _);
    }
}