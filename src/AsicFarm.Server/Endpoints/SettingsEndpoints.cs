using AsicFarm.Server.Services;
using AsicFarm.Shared.Contracts;

namespace AsicFarm.Server.Endpoints;

public static class SettingsEndpoints
{
    private const string KeyChatId = "Telegram:ChatId";

    public static void MapSettings(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/settings", GetAsync);
        app.MapPut("/api/v1/settings", PutAsync);
        app.MapPost("/api/v1/settings/telegram/test", TestTelegramAsync);
    }

    private static async Task<IResult> GetAsync(
        SettingsService settings,
        IConfiguration config,
        CancellationToken ct)
    {
        var chat = await settings.GetAsync(KeyChatId, ct);
        if (string.IsNullOrWhiteSpace(chat))
            chat = config["Telegram:DefaultChatId"];

        var botToken = config["Telegram:BotToken"];
        var botOk = !string.IsNullOrWhiteSpace(botToken);

        var dto = new SettingsDto
        {
            TelegramChatId = chat,
            BotTokenPresent = botOk,
            TelegramConfigured = botOk && !string.IsNullOrWhiteSpace(chat),
        };
        return Results.Ok(dto);
    }

    private static async Task<IResult> PutAsync(
        SettingsUpdateRequest req,
        SettingsService settings,
        CancellationToken ct)
    {
        if (req.TelegramChatId is not null)
            await settings.SetAsync(KeyChatId, req.TelegramChatId, ct);

        return Results.Ok(new { saved = true });
    }

    private static async Task<IResult> TestTelegramAsync(
        TelegramNotifier telegram, CancellationToken ct)
    {
        var (ok, err) = await telegram.SendAsync(null,
            "✅ <b>AsicFarm</b>: тестовое сообщение. Всё работает!",
            ct);

        return ok
            ? Results.Ok(new SettingsTestResult(true, null))
            : Results.Json(new SettingsTestResult(false, err), statusCode: 500);
    }
}