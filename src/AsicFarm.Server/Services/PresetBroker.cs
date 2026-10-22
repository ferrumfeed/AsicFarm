using System.Collections.Concurrent;
using AsicFarm.Shared.Vnish;

namespace AsicFarm.Server.Services;

public sealed class PresetBroker
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<List<VnishAutotunePreset>>> _pending = new();

    public TaskCompletionSource<List<VnishAutotunePreset>> Register(string requestId)
    {
        var tcs = new TaskCompletionSource<List<VnishAutotunePreset>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = tcs;
        return tcs;
    }

    public void Complete(string requestId, List<VnishAutotunePreset> presets)
    {
        if (_pending.TryRemove(requestId, out var tcs))
            tcs.TrySetResult(presets);
    }

    public void Remove(string requestId) => _pending.TryRemove(requestId, out _);
}