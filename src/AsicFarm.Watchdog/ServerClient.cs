using System.Net.Http.Json;
using AsicFarm.Shared.Contracts;
using AsicFarm.Shared.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AsicFarm.Watchdog;

public sealed class ServerClient
{
    private readonly HttpClient _http;
    private readonly WatchdogOptions _opt;
    private readonly ILogger<ServerClient> _log;

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_opt.ServerUrl);

    public ServerClient(HttpClient http, IOptions<WatchdogOptions> options, ILogger<ServerClient> log)
    {
        _http = http;
        _opt = options.Value;
        _log = log;

        if (IsEnabled)
        {
            _http.BaseAddress = new Uri(_opt.ServerUrl!);
            if (!string.IsNullOrWhiteSpace(_opt.ServerApiKey))
                _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", _opt.ServerApiKey);
        }
    }

    public async Task SendBatchAsync(IReadOnlyList<MinerSnapshot> snapshots, CancellationToken ct)
    {
        if (!IsEnabled || snapshots.Count == 0) return;

        var payload = new IngestBatchRequest(
            WatchdogId: _opt.WatchdogId,
            SentAt: DateTimeOffset.UtcNow,
            Snapshots: snapshots);

        try
        {
            using var resp = await _http.PostAsJsonAsync("/api/v1/ingest/snapshots", payload, ct)
                                        .ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _log.LogWarning("Server rejected batch: HTTP {Code} {Body}",
                    (int)resp.StatusCode, body);
                return;
            }

            var result = await resp.Content.ReadFromJsonAsync<IngestBatchResponse>(ct)
                                 .ConfigureAwait(false);

            _log.LogInformation("Batch sent: {Sent} → accepted {Accepted}, rejected {Rejected}",
                snapshots.Count, result?.Accepted ?? 0, result?.Rejected ?? 0);
        }
        catch (HttpRequestException ex)
        {
            _log.LogWarning("Server unreachable: {Msg}", ex.Message);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning("Server timeout");
        }
    }
}