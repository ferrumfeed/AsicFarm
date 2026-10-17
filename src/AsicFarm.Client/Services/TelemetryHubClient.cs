using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace AsicFarm.Client.Services;

public sealed record MinerUpdate(
    string MinerId,
    string Ip,
    string Mac,
    string Hostname,
    string Model,
    string ModelCode,
    string FirmwareName,
    string FirmwareVersion,
    string WatchdogId,
    string State,
    double HashrateGhs,
    double HashratePercent,
    int ChipMaxTemp,
    int PowerW,
    DateTimeOffset Timestamp);

public sealed record MinerRemoved(
    string MinerId,
    string Reason,
    DateTimeOffset? LastSeen);

public sealed class TelemetryHubClient : IAsyncDisposable
{
    private readonly ILogger<TelemetryHubClient> _log;
    private HubConnection? _conn;

    public event Action<MinerUpdate>? MinerUpdated;
    public event Action<MinerRemoved>? MinerRemoved;
    public event Action<bool>? ConnectionChanged;

    public TelemetryHubClient(ILogger<TelemetryHubClient> log)
    {
        _log = log;
    }

    public bool IsConnected => _conn?.State == HubConnectionState.Connected;

    public async Task ConnectAsync(string baseUrl, string? apiKey, CancellationToken ct = default)
    {
        await DisconnectAsync();

        var url = baseUrl.TrimEnd('/') + "/hubs/telemetry";

        _conn = new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                if (!string.IsNullOrWhiteSpace(apiKey))
                    options.Headers.Add("X-Api-Key", apiKey);

                options.Transports =
                    HttpTransportType.WebSockets |
                    HttpTransportType.ServerSentEvents |
                    HttpTransportType.LongPolling;
            })
            .WithAutomaticReconnect(new[]
            {
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10),
            })
            .Build();

        _conn.On<MinerUpdate>("miner.updated", update =>
        {
            try { MinerUpdated?.Invoke(update); }
            catch (Exception ex) { _log.LogWarning(ex, "Error in MinerUpdated handler"); }
        });

        _conn.On<MinerRemoved>("miner.removed", removed =>
        {
            try { MinerRemoved?.Invoke(removed); }
            catch (Exception ex) { _log.LogWarning(ex, "Error in MinerRemoved handler"); }
        });

        _conn.Reconnected += _ =>
        {
            ConnectionChanged?.Invoke(true);
            return Task.CompletedTask;
        };
        _conn.Reconnecting += _ =>
        {
            ConnectionChanged?.Invoke(false);
            return Task.CompletedTask;
        };
        _conn.Closed += _ =>
        {
            ConnectionChanged?.Invoke(false);
            return Task.CompletedTask;
        };

        try
        {
            await _conn.StartAsync(ct);
            ConnectionChanged?.Invoke(true);
            _log.LogInformation("Connected to hub {Url}", url);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Hub connect failed");
            ConnectionChanged?.Invoke(false);
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        if (_conn is not null)
        {
            try { await _conn.DisposeAsync(); } catch { }
            _conn = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }
}