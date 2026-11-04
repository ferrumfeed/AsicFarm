namespace AsicFarm.Watchdog.Services;

public sealed class SstpClientOptions
{
    public bool Enabled { get; set; }
    public string ConnectionName { get; set; } = "AsicFarm-SSTP";
    public string Server { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    /// <summary>
    /// Подсеть асиков, до которой должен быть маршрут через SSTP.
    /// Используется для диагностики: если профиль не настроен на split tunneling,
    /// в лог попадёт предупреждение.
    /// </summary>
    public string MinerSubnet { get; set; } = "192.168.2.0/24";

    public int StartupWaitSeconds { get; set; } = 10;

    /// <summary>
    /// Хост для периодической проверки живости туннеля (например, IP Keenetic в LAN).
    /// Если пусто — проверка отключена.
    /// </summary>
    public string HealthCheckHost { get; set; } = "";
    public int HealthCheckIntervalSeconds { get; set; } = 60;
}