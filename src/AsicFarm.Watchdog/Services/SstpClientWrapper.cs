using System.Diagnostics;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Options;

namespace AsicFarm.Watchdog.Services;

public sealed class SstpClientWrapper : IAsyncDisposable
{
    private readonly SstpClientOptions _opt;
    private readonly ILogger<SstpClientWrapper> _log;
    private CancellationTokenSource? _healthCts;
    private Task? _healthTask;

    public SstpClientWrapper(IOptions<SstpClientOptions> options, ILogger<SstpClientWrapper> log)
    {
        _opt = options.Value;
        _log = log;
    }

    public bool IsEnabled => _opt.Enabled;

    public async Task StartAsync(CancellationToken ct)
    {
        if (!_opt.Enabled)
        {
            _log.LogInformation("SSTP client disabled in config, skipping");
            return;
        }

        if (string.IsNullOrWhiteSpace(_opt.ConnectionName))
            throw new InvalidOperationException("SstpClient.Enabled=true, but ConnectionName is empty");

        _log.LogInformation("Connecting SSTP VPN '{Connection}'...", _opt.ConnectionName);

        var args = $"{_opt.ConnectionName} {_opt.Username} {_opt.Password}";
        var exitCode = await RunRasdialAsync(args, ct);

        if (exitCode != 0)
            throw new InvalidOperationException($"rasdial connect failed with exit code {exitCode}");

        _log.LogInformation("SSTP VPN connected, waiting {Sec}s...", _opt.StartupWaitSeconds);
        await Task.Delay(TimeSpan.FromSeconds(_opt.StartupWaitSeconds), ct);

        if (!await IsConnectionActiveAsync(ct))
            throw new InvalidOperationException("SSTP VPN connection is not active after rasdial");

        // >>> Диагностика split tunneling: не даём туннелю перехватить весь трафик
        await WarnIfSplitTunnelingDisabledAsync(ct);

        _log.LogInformation("SSTP tunnel is up (miner subnet: {Subnet})", _opt.MinerSubnet);

        if (!string.IsNullOrWhiteSpace(_opt.HealthCheckHost))
        {
            _healthCts = new CancellationTokenSource();
            _healthTask = RunHealthCheckLoopAsync(_healthCts.Token);
        }
    }

    private async Task<int> RunRasdialAsync(string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("rasdial", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null)
            throw new InvalidOperationException("Failed to start rasdial");

        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
            _log.LogError("rasdial stderr: {Error}", stderr);

        return process.ExitCode;
    }

    private async Task<bool> IsConnectionActiveAsync(CancellationToken ct)
    {
        var psi = new ProcessStartInfo("rasdial")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null) return false;

        var output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return output.Contains(_opt.ConnectionName, StringComparison.OrdinalIgnoreCase);
    }

    private async Task WarnIfSplitTunnelingDisabledAsync(CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo("powershell",
                $"-NoProfile -NonInteractive -Command \"(Get-VpnConnection -Name '{_opt.ConnectionName}').SplitTunneling\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            if (p is null) return;

            var output = (await p.StandardOutput.ReadToEndAsync(ct)).Trim();
            await p.WaitForExitAsync(ct);

            if (string.Equals(output, "False", StringComparison.OrdinalIgnoreCase))
            {
                _log.LogWarning(
                    "SSTP connection '{Connection}' has SplitTunneling=False — ALL traffic will go through VPN. " +
                    "Fix it with: Set-VpnConnection -Name '{Connection}' -SplitTunneling $true",
                    _opt.ConnectionName, _opt.ConnectionName);
            }
            else if (string.Equals(output, "True", StringComparison.OrdinalIgnoreCase))
            {
                _log.LogInformation("SSTP split tunneling enabled — only {Subnet} goes through VPN", _opt.MinerSubnet);
            }
            else
            {
                _log.LogDebug("Could not determine SplitTunneling state (output: '{Output}')", output);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to check split tunneling state (non-fatal)");
        }
    }

    private async Task RunHealthCheckLoopAsync(CancellationToken ct)
    {
        var host = _opt.HealthCheckHost;
        var interval = TimeSpan.FromSeconds(Math.Max(10, _opt.HealthCheckIntervalSeconds));

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, ct);

                if (!await IsConnectionActiveAsync(ct))
                {
                    _log.LogError("SSTP connection '{Connection}' is not active", _opt.ConnectionName);
                    return;
                }

                using var ping = new Ping();
                var reply = await ping.SendPingAsync(host, TimeSpan.FromSeconds(5));
                if (reply.Status == IPStatus.Success)
                    _log.LogDebug("SSTP health check OK ({Host}, {Ms}ms)", host, reply.RoundtripTime);
                else
                    _log.LogWarning("SSTP health check failed: {Status} to {Host}", reply.Status, host);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "SSTP health check error");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _healthCts?.Cancel();
        if (_healthTask is not null)
        {
            try { await _healthTask; } catch { /* ignore */ }
        }
        _healthCts?.Dispose();

        if (_opt.Enabled)
        {
            _log.LogInformation("Disconnecting SSTP VPN '{Connection}'...", _opt.ConnectionName);
            var exitCode = await RunRasdialAsync($"{_opt.ConnectionName} /disconnect", CancellationToken.None);
            if (exitCode != 0)
                _log.LogWarning("rasdial disconnect exited with code {Code}", exitCode);
        }

        _log.LogInformation("SSTP client stopped");
    }
}