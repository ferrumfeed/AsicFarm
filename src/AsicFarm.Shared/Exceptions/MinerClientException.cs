namespace AsicFarm.Shared.Exceptions;

public class MinerClientException : Exception
{
    public MinerClientException(string message) : base(message) { }
    public MinerClientException(string message, Exception inner) : base(message, inner) { }
}

public sealed class MinerUnreachableException : MinerClientException
{
    public string Host { get; }

    public MinerUnreachableException(string host, Exception inner)
        : base($"Miner '{host}' is unreachable", inner)
    {
        Host = host;
    }
}

public sealed class MinerAuthException : MinerClientException
{
    public string Host { get; }

    public MinerAuthException(string host, string reason)
        : base($"Miner '{host}' auth failed: {reason}")
    {
        Host = host;
    }
}

public sealed class MinerApiException : MinerClientException
{
    public int StatusCode { get; }
    public string? ResponseBody { get; }

    public MinerApiException(string host, int statusCode, string? body)
        : base($"Miner '{host}' returned {statusCode}: {Truncate(body)}")
    {
        StatusCode = statusCode;
        ResponseBody = body;
    }

    private static string Truncate(string? s) =>
        s is null ? "<empty>" : s.Length > 512 ? s[..512] + "…" : s;
}