using System.Net;
using System.Net.Http;
using AsicFarm.Shared.Vnish;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AsicFarm.Watchdog.Services;

public sealed class SubnetScanner : BackgroundService
{
    private readonly WatchdogOptions _opt;
    private readonly MinerHostRegistry _registry;
    private readonly ILogger<SubnetScanner> _log;

    public SubnetScanner(
        IOptions<WatchdogOptions> options,
        MinerHostRegistry registry,
        ILogger<SubnetScanner> log)
    {
        _opt = options.Value;
        _registry = registry;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _registry.AddRange(_opt.Hosts);
        _log.LogInformation("Static hosts loaded: {Count}", _opt.Hosts.Count);

        if (_opt.Subnets.Count > 0)
            await ScanAllAsync(stoppingToken);
        else
            _log.LogInformation("No subnets configured — авто-скан отключён");

        var interval = TimeSpan.FromMinutes(Math.Max(1, _opt.RescanIntervalMinutes));
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            if (_opt.Subnets.Count > 0)
                await ScanAllAsync(stoppingToken);
        }
    }

    private async Task ScanAllAsync(CancellationToken ct)
    {
        var candidates = new List<string>();

        foreach (var subnet in _opt.Subnets)
        {
            try
            {
                var ips = ExpandCidr(subnet).ToList();
                _log.LogInformation("Subnet {Subnet}: {Count} адресов", subnet, ips.Count);
                candidates.AddRange(ips);
            }
            catch (Exception ex)
            {
                _log.LogWarning("Subnet {Subnet}: неверный CIDR — {Msg}", subnet, ex.Message);
            }
        }

        if (candidates.Count == 0) return;

        _log.LogInformation("Начинаю скан {Total} адресов...", candidates.Count);

        var found = 0;
        var parallel = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, _opt.ScanConcurrency),
            CancellationToken = ct,
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();

        await Parallel.ForEachAsync(candidates, parallel, async (ip, innerCt) =>
        {
            if (await ProbeAsync(ip, innerCt).ConfigureAwait(false))
            {
                _registry.Add(ip);
                Interlocked.Increment(ref found);
            }
        }).ConfigureAwait(false);

        sw.Stop();
        _log.LogInformation("Скан завершён за {Sec:F1} сек: найдено {Found}, всего в реестре {Total}",
            sw.Elapsed.TotalSeconds, found, _registry.AllHosts.Count);
    }

    private async Task<bool> ProbeAsync(string ip, CancellationToken ct)
    {
        using var handler = new SocketsHttpHandler
        {
            ConnectTimeout = _opt.ScanTimeout,
            PooledConnectionLifetime = TimeSpan.FromSeconds(30),
        };
        using var http = new HttpClient(handler) { Timeout = _opt.ScanTimeout };

        try
        {
            var url = $"http://{ip}/api/v1/info";
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseContentRead, ct)
                                      .ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode) return false;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var info = await System.Text.Json.JsonSerializer
                .DeserializeAsync<VnishInfo>(stream, VnishJson.Options, ct)
                .ConfigureAwait(false);

            return info?.System?.Network is not null;
        }
        catch
        {
            return false;
        }
    }

    /// Разворачивает CIDR (192.168.2.0/24) в список IP. Пропускает сеть и broadcast.
    public static IEnumerable<string> ExpandCidr(string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2)
            throw new ArgumentException($"Неверный CIDR: {cidr}");

        var baseIp = IPAddress.Parse(parts[0]);
        var prefix = int.Parse(parts[1]);

        if (prefix < 16 || prefix > 32)
            throw new ArgumentException($"Допустимый префикс: /16../32. Получено: /{prefix}");

        var bytes = baseIp.GetAddressBytes();
        var baseUint = ((uint)bytes[0] << 24)
                     | ((uint)bytes[1] << 16)
                     | ((uint)bytes[2] << 8)
                     | bytes[3];

        if (prefix == 32)
        {
            yield return baseIp.ToString();
            yield break;
        }

        var mask = 0xFFFFFFFFu << (32 - prefix);
        var network = baseUint & mask;
        var broadcast = network | ~mask;

        var count = broadcast - network - 1;
        if (count > 1024)
            throw new ArgumentException($"Подсеть слишком большая: {count} адресов. Максимум 1024.");

        for (var ip = network + 1; ip < broadcast; ip++)
        {
            yield return new IPAddress(new[]
            {
                (byte)(ip >> 24),
                (byte)(ip >> 16),
                (byte)(ip >> 8),
                (byte)ip,
            }).ToString();
        }
    }
}