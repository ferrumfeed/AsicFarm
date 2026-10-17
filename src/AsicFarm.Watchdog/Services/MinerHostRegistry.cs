using System.Collections.Concurrent;

namespace AsicFarm.Watchdog.Services;

/// Реестр хостов, которые нужно опрашивать.
/// Заполняется статикой из конфига и сканером подсетей.
public sealed class MinerHostRegistry
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _hosts = new();

    public void Add(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return;
        _hosts[Normalize(host)] = DateTimeOffset.UtcNow;
    }

    public void AddRange(IEnumerable<string> hosts)
    {
        foreach (var h in hosts) Add(h);
    }

    public void MarkAlive(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return;
        _hosts[Normalize(host)] = DateTimeOffset.UtcNow;
    }

    public void RemoveStale(TimeSpan ttl)
    {
        var cutoff = DateTimeOffset.UtcNow - ttl;
        foreach (var kv in _hosts)
        {
            if (kv.Value < cutoff)
                _hosts.TryRemove(kv.Key, out _);
        }
    }

    public IReadOnlyList<string> AllHosts => _hosts.Keys.ToList();

    private static string Normalize(string host)
    {
        host = host.Trim();
        if (host.EndsWith(":80", StringComparison.Ordinal))
            return host[..^3];
        return host;
    }
}