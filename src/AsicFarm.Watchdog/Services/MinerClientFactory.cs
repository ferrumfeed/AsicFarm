using AsicFarm.MinerClients.Vnish;
using AsicFarm.MinerClients.Whatsminer;
using AsicFarm.Shared.Contracts;
using AsicFarm.Shared.Domain;
using Microsoft.Extensions.Options;

namespace AsicFarm.Watchdog.Services;

public sealed class MinerClientFactory
{
    private readonly WatchdogOptions _opt;
    private readonly ILoggerFactory _loggerFactory;

    public MinerClientFactory(IOptions<WatchdogOptions> opt, ILoggerFactory loggerFactory)
    {
        _opt = opt.Value;
        _loggerFactory = loggerFactory;
    }

    public IAntminerClient Create(string host, FirmwareKind firmware, int? port = null)
    {
        return firmware switch
        {
            FirmwareKind.Vnish => CreateVnish(host, port),
            FirmwareKind.Whatsminer => CreateWhatsminer(host),
            _ => throw new NotSupportedException($"Firmware {firmware} is not supported for {host}")
        };
    }

    private IAntminerClient CreateVnish(string host, int? port)
    {
        var options = new VnishAntminerClientOptions
        {
            Host = host,
            Port = port ?? 80,
            Password = _opt.Password,
            // ApiKey не ставим: оригинальный MinerPoller использовал только Password
        };

        return new VnishAntminerClient(
            options,
            logger: _loggerFactory.CreateLogger<VnishAntminerClient>());
    }

    private IAntminerClient CreateWhatsminer(string host)
    {
        return new WhatsminerAntminerClient(
            host,
            adminPassword: _opt.WhatsminerAdminPassword,
            logger: _loggerFactory.CreateLogger<WhatsminerAntminerClient>());
    }
}