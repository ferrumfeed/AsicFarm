using System.Collections.Concurrent;
using AsicFarm.Shared.Domain;

namespace AsicFarm.Watchdog.Services;

public sealed class MinerFirmwareRegistry
{
    private readonly ConcurrentDictionary<string, FirmwareKind> _map = new(StringComparer.OrdinalIgnoreCase);

    public void Set(string host, FirmwareKind firmware) => _map[host] = firmware;

    public FirmwareKind Get(string host)
        => _map.TryGetValue(host, out var fw) ? fw : FirmwareKind.Unknown;

    public IReadOnlyDictionary<string, FirmwareKind> All => _map;
}