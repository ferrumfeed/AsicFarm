using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;

namespace AsicFarm.Client.Services;

public static class PingService
{
    // TCP-пинг (для пулов)
    public static async Task<long> TcpPingAsync(string host, int port, int timeoutMs = 3000)
    {
        using var client = new TcpClient();
        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            var sw = Stopwatch.StartNew();
            await client.ConnectAsync(host, port, cts.Token);
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }
        catch
        {
            return -1; // -1 означает ошибку или таймаут
        }
    }

    // HTTP-пинг (для сервера)
    public static async Task<long> HttpPingAsync(HttpClient httpClient, string url, int timeoutMs = 3000)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            var sw = Stopwatch.StartNew();
            // Стучимся в /health или любой легкий эндпоинт
            using var resp = await httpClient.GetAsync(url.TrimEnd('/') + "/health", HttpCompletionOption.ResponseHeadersRead, cts.Token);
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }
        catch
        {
            return -1;
        }
    }
}