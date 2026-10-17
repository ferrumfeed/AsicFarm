namespace AsicFarm.Server.Auth;

public sealed class ServerOptions
{
    public const string SectionName = "Server";
    public string ApiKey { get; set; } = "";
    public string DatabasePath { get; set; } = "asicfarm.db";

    /// Через сколько минут без новых снапшотов удалять майнера.
    /// По умолчанию 3 минуты (6 циклов опроса по 30 сек).
    public int MinerTtlMinutes { get; set; } = 3;

    /// Через сколько минут без heartbeat удалять ватчдог.
    /// По умолчанию 15 минут.
    public int WatchdogTtlMinutes { get; set; } = 15;
}