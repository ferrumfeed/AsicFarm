using System.Security.Cryptography;
using System.Text;

namespace AsicFarm.MinerClients.Whatsminer;

public sealed class WhatsminerTokenManager
{
    private readonly string _adminPassword;
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;

    public WhatsminerTokenManager(string adminPassword)
    {
        _adminPassword = adminPassword;
    }

    public async Task<string> GetTokenAsync(WhatsminerTcpTransport transport, CancellationToken ct)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiry)
            return _cachedToken;

        using var info = await transport.SendCommandAsync(new { cmd = "get.device.info" }, TimeSpan.FromSeconds(10), ct);
        var root = info.RootElement;

        if (root.GetProperty("code").GetInt32() != 0)
            throw new InvalidOperationException(
                $"Whatsminer: get.device.info failed with code {root.GetProperty("code").GetInt32()}");

        var msg = root.GetProperty("msg");
        if (!msg.TryGetProperty("salt", out var saltProp))
            throw new InvalidOperationException("Whatsminer: get.device.info did not return salt");

        var salt = saltProp.GetString()
            ?? throw new InvalidOperationException("Whatsminer: salt is empty");

        _cachedToken = GenerateToken(salt, _adminPassword);

        // Токен Whatsminer живёт ~30 минут. Обновляем за 5 минут до истечения.
        _tokenExpiry = DateTimeOffset.UtcNow.AddMinutes(25);
        return _cachedToken;
    }

    /// <summary>
    /// Генерация токена для Whatsminer API v3.0+.
    /// Формула: sha256(password + salt), hex в нижнем регистре.
    /// Если майнер возвращает code=-4 (no permission) — попробуйте варианты:
    ///   - sha256(salt + password)
    ///   - md5(password + salt)
    /// </summary>
    private static string GenerateToken(string salt, string password)
    {
        var input = Encoding.ASCII.GetBytes(password + salt);
        var hash = SHA256.HashData(input);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public void Invalidate()
    {
        _cachedToken = null;
        _tokenExpiry = DateTimeOffset.MinValue;
    }
}