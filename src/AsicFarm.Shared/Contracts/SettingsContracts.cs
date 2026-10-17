namespace AsicFarm.Shared.Contracts;

public sealed record SettingsDto
{
    public string? TelegramChatId { get; init; }
    public bool TelegramConfigured { get; init; }
    public bool BotTokenPresent { get; init; }
}

public sealed record SettingsUpdateRequest
{
    public string? TelegramChatId { get; init; }
}

public sealed record SettingsTestResult(bool Ok, string? Error);