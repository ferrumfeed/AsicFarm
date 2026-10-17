using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AsicFarm.Client.Services;

public sealed record BitcoinStats(
    decimal BtcPriceUsd,
    double NetworkDifficulty,
    DateTimeOffset FetchedAt)
{
    public static readonly BitcoinStats Empty = new(0, 0, DateTimeOffset.MinValue);
}

public sealed class BitcoinNetworkService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly ILogger<BitcoinNetworkService> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _cacheTtl = TimeSpan.FromMinutes(5);

    private BitcoinStats _cache = BitcoinStats.Empty;

    public BitcoinNetworkService(ILogger<BitcoinNetworkService> log)
    {
        _log = log;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AsicFarm/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public async Task<BitcoinStats> GetStatsAsync(CancellationToken ct = default)
    {
        if (DateTimeOffset.UtcNow - _cache.FetchedAt < _cacheTtl && _cache.BtcPriceUsd > 0)
            return _cache;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (DateTimeOffset.UtcNow - _cache.FetchedAt < _cacheTtl && _cache.BtcPriceUsd > 0)
                return _cache;

            var priceTask = FetchPriceAsync(ct);
            var diffTask = FetchDifficultyAsync(ct);
            await Task.WhenAll(priceTask, diffTask).ConfigureAwait(false);

            var price = await priceTask.ConfigureAwait(false);
            var diff = await diffTask.ConfigureAwait(false);

            // Сохраняем даже частичный результат — не затираем удачное значение нулём
            var newPrice = price > 0 ? price : _cache.BtcPriceUsd;
            var newDiff = diff > 0 ? diff : _cache.NetworkDifficulty;

            if (newPrice > 0 || newDiff > 0)
            {
                _cache = new BitcoinStats(newPrice, newDiff, DateTimeOffset.UtcNow);
                _log.LogInformation("BTC=${Price} difficulty={Diff}",
                    newPrice.ToString("F0"), newDiff);
            }

            return _cache;
        }
        finally { _gate.Release(); }
    }

    // =================== ЦЕНА ===================

    private async Task<decimal> FetchPriceAsync(CancellationToken ct)
    {
        // 1) blockchain.info
        var p1 = await TryBlockchainInfoPriceAsync(ct);
        if (p1 > 0) { _log.LogInformation("Price from blockchain.info: {P}", p1); return p1; }

        // 2) coinbase
        var p2 = await TryCoinbasePriceAsync(ct);
        if (p2 > 0) { _log.LogInformation("Price from coinbase: {P}", p2); return p2; }

        // 3) coindesk
        var p3 = await TryCoindeskPriceAsync(ct);
        if (p3 > 0) { _log.LogInformation("Price from coindesk: {P}", p3); return p3; }

        _log.LogWarning("Все источники цены BTC недоступны");
        return 0;
    }

    private async Task<decimal> TryBlockchainInfoPriceAsync(CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync("https://blockchain.info/ticker", ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            if (doc.RootElement.TryGetProperty("USD", out var usd) &&
                usd.TryGetProperty("last", out var last))
                return last.GetDecimal();
        }
        catch (Exception ex) { _log.LogDebug("blockchain.info price failed: {Msg}", ex.Message); }
        return 0;
    }

    private async Task<decimal> TryCoinbasePriceAsync(CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync(
                "https://api.coinbase.com/v2/prices/BTC-USD/spot", ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("amount", out var amount))
            {
                var s = amount.GetString();
                if (decimal.TryParse(s, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var v))
                    return v;
            }
        }
        catch (Exception ex) { _log.LogDebug("coinbase price failed: {Msg}", ex.Message); }
        return 0;
    }

    private async Task<decimal> TryCoindeskPriceAsync(CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync(
                "https://api.coindesk.com/v1/bpi/currentprice.json", ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            if (doc.RootElement.TryGetProperty("bpi", out var bpi) &&
                bpi.TryGetProperty("USD", out var usd) &&
                usd.TryGetProperty("rate_float", out var rate))
                return rate.GetDecimal();
        }
        catch (Exception ex) { _log.LogDebug("coindesk price failed: {Msg}", ex.Message); }
        return 0;
    }

    // =================== СЛОЖНОСТЬ ===================

    private async Task<double> FetchDifficultyAsync(CancellationToken ct)
    {
        // 1) blockchain.info
        var d1 = await TryBlockchainInfoDifficultyAsync(ct);
        if (d1 > 0) { _log.LogInformation("Difficulty from blockchain.info: {D}", d1); return d1; }

        // 2) mempool.space
        var d2 = await TryMempoolDifficultyAsync(ct);
        if (d2 > 0) { _log.LogInformation("Difficulty from mempool.space: {D}", d2); return d2; }

        _log.LogWarning("Все источники сложности недоступны");
        return 0;
    }

    private async Task<double> TryBlockchainInfoDifficultyAsync(CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync(
                "https://blockchain.info/q/getdifficulty", ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            var str = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (double.TryParse(str.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var d))
                return d;
        }
        catch (Exception ex) { _log.LogDebug("blockchain.info difficulty failed: {Msg}", ex.Message); }
        return 0;
    }

    private async Task<double> TryMempoolDifficultyAsync(CancellationToken ct)
    {
        try
        {
            // mempool.space отдаёт массив последних блоков; у каждого — difficulty
            using var resp = await _http.GetAsync(
                "https://mempool.space/api/v1/blocks", ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            if (doc.RootElement.ValueKind == JsonValueKind.Array &&
                doc.RootElement.GetArrayLength() > 0)
            {
                var first = doc.RootElement[0];
                if (first.TryGetProperty("difficulty", out var diff))
                    return diff.GetDouble();
            }
        }
        catch (Exception ex) { _log.LogDebug("mempool.space difficulty failed: {Msg}", ex.Message); }
        return 0;
    }

    public void Dispose() => _http.Dispose();
}