using System.Collections.Concurrent;

namespace AsicFarm.Watchdog.Services;

/// Реестр: MAC → IP. Заполняется MinerPoller при каждом опросе.
public sealed class MinerRegistry
{
    private readonly ConcurrentDictionary<string, string> _macToIp = new();
    private readonly ConcurrentDictionary<string, string> _ipToMac = new();

    public void Register(string mac, string ip)
    {
        if (string.IsNullOrWhiteSpace(mac) || string.IsNullOrWhiteSpace(ip)) return;
        _macToIp[mac] = ip;
        _ipToMac[ip] = mac;
    }

    public string? TryGetIp(string mac) =>
        _macToIp.TryGetValue(mac, out var ip) ? ip : null;

    public string? TryGetMac(string ip) =>
        _ipToMac.TryGetValue(ip, out var mac) ? mac : null;

    public IReadOnlyDictionary<string, string> AllMacToIp => _macToIp;
}