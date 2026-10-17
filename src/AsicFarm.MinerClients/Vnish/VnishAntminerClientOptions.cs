namespace AsicFarm.MinerClients.Vnish;

public sealed class VnishAntminerClientOptions
{
    public required string Host { get; init; }
    public int Port { get; init; } = 80;
    public bool UseHttps { get; init; }

    public string? Password { get; init; }
    public string? ApiKey { get; init; }

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public Uri BaseAddress
    {
        get
        {
            var scheme = UseHttps ? "https" : "http";
            var authority = Port is 80 or 443 ? Host : $"{Host}:{Port}";
            return new Uri($"{scheme}://{authority}/api/v1/");
        }
    }
}