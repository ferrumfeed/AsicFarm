namespace AsicFarm.MinerClients.Vnish;

internal sealed class VnishAuthState(string? apiKey)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;

    public bool HasStaticApiKey => !string.IsNullOrEmpty(apiKey);
    public string? ApiKey => apiKey;

    public async Task<string> GetOrAcquireTokenAsync(
        Func<CancellationToken, Task<string>> acquire,
        CancellationToken ct)
    {
        if (_token is { Length: > 0 }) return _token;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_token is { Length: > 0 }) return _token;
            _token = await acquire(ct).ConfigureAwait(false);
            return _token;
        }
        finally { _gate.Release(); }
    }

    public void Invalidate() => _token = null;
}