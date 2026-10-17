namespace AsicFarm.Watchdog;

public sealed class WatchdogOptions
{
    public const string SectionName = "Watchdog";

    /// Ручной список хостов. Формат: "192.168.2.101" или "192.168.2.101:8080"
    public List<string> Hosts { get; set; } = new();

    /// Подсети для авто-сканирования в формате CIDR: "192.168.2.0/24".
    public List<string> Subnets { get; set; } = new();

    /// Как часто пересканировать подсети (в минутах).
    public int RescanIntervalMinutes { get; set; } = 15;

    public string? Password { get; set; }

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(20);

    public int MaxConcurrency { get; set; } = 16;

    /// Параллелизм при сканировании подсети.
    public int ScanConcurrency { get; set; } = 32;

    /// Таймаут на один IP при сканировании.
    public TimeSpan ScanTimeout { get; set; } = TimeSpan.FromMilliseconds(800);

    public bool VerboseSnapshotLog { get; set; } = true;

    public string? ServerUrl { get; set; }
    public string? ServerApiKey { get; set; }
    public string WatchdogId { get; set; } = "watchdog-default";
}