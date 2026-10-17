using System.Net;
using System.Net.Http.Json;

namespace AsicFarm.Server.Services;

public sealed class TelegramNotifier
{
    private readonly SettingsService _settings;
    private readonly IConfiguration _config;
    private readonly ILogger<TelegramNotifier> _log;

    private readonly object _httpLock = new();
    private HttpClient? _http;

    public TelegramNotifier(
        SettingsService settings,
        IConfiguration config,
        ILogger<TelegramNotifier> log)
    {
        _settings = settings;
        _config = config;
        _log = log;
    }

    /// Bot Token — только из appsettings.json сервера (не из UI, не из БД).
    private string? BotToken => _config["Telegram:BotToken"];

    public async Task<bool> IsEnabledAsync(CancellationToken ct = default)
    {
        var token = BotToken;
        if (string.IsNullOrWhiteSpace(token)) return false;

        var chat = await _settings.GetAsync("Telegram:ChatId", ct);
        return !string.IsNullOrWhiteSpace(chat);
    }

    public async Task<(bool Ok, string? Error)> SendAsync(
        string? chatIdOverride,
        string text,
        CancellationToken ct)
    {
        var token = BotToken;
        if (string.IsNullOrWhiteSpace(token))
            return (false, "Bot Token не задан на сервере (appsettings.json → Telegram:BotToken)");

        // chat_id: либо явно переданный (rule.ChannelConfig), либо из настроек БД, либо из appsettings
        var chatId = chatIdOverride;
        if (string.IsNullOrWhiteSpace(chatId))
            chatId = await _settings.GetAsync("Telegram:ChatId", ct);
        if (string.IsNullOrWhiteSpace(chatId))
            chatId = _config["Telegram:DefaultChatId"];

        if (string.IsNullOrWhiteSpace(chatId))
            return (false, "Chat ID не указан — введи его в Настройках в UI");

        try
        {
            var http = GetHttpClient();
            var url = $"https://api.telegram.org/bot{token}/sendMessage";
            var body = new
            {
                chat_id = chatId,
                text,
                parse_mode = "HTML",
                disable_web_page_preview = true,
            };

            using var resp = await http.PostAsJsonAsync(url, body, ct).ConfigureAwait(false);
            var respBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("Telegram send failed: HTTP {Code} {Body}",
                    (int)resp.StatusCode, respBody);
                return (false, $"HTTP {(int)resp.StatusCode}: {Truncate(respBody)}");
            }

            return (true, null);
        }
        catch (TaskCanceledException)
        {
            return (false, "Timeout — Telegram недоступен. Попробуй позже.");
        }
        catch (HttpRequestException ex)
        {
            _log.LogWarning(ex, "Telegram HTTP error");
            return (false, $"HTTP: {ex.Message}");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Telegram send error");
            return (false, ex.Message);
        }
    }

    private HttpClient GetHttpClient()
    {
        lock (_httpLock)
        {
            if (_http is not null) return _http;

            var handler = new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(15),
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            };

            _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            return _http;
        }
    }

    private static string Truncate(string s) =>
        string.IsNullOrEmpty(s) ? "" : s.Length > 200 ? s[..200] + "…" : s;
}