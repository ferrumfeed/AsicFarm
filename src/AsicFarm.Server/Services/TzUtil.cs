namespace AsicFarm.Server.Services;

public static class TzUtil
{
    /// Хардкод смещений для известных IANA-зон.
    /// Проверяется ПЕРВЫМ — гарантированно работает на любой Windows.
    private static readonly Dictionary<string, TimeSpan> FixedOffsets = new(StringComparer.OrdinalIgnoreCase)
    {
        // Россия
        ["Europe/Kaliningrad"] = TimeSpan.FromHours(2),
        ["Europe/Moscow"] = TimeSpan.FromHours(3),
        ["Europe/Samara"] = TimeSpan.FromHours(4),
        ["Asia/Yekaterinburg"] = TimeSpan.FromHours(5),
        ["Asia/Omsk"] = TimeSpan.FromHours(6),
        ["Asia/Krasnoyarsk"] = TimeSpan.FromHours(7),
        ["Asia/Irkutsk"] = TimeSpan.FromHours(8),
        ["Asia/Yakutsk"] = TimeSpan.FromHours(9),
        ["Asia/Vladivostok"] = TimeSpan.FromHours(10),
        ["Asia/Magadan"] = TimeSpan.FromHours(11),
        ["Asia/Kamchatka"] = TimeSpan.FromHours(12),
        ["Asia/Sakhalin"] = TimeSpan.FromHours(11),

        // Соседи
        ["Asia/Almaty"] = TimeSpan.FromHours(5),
        ["Asia/Tashkent"] = TimeSpan.FromHours(5),
        ["Asia/Tbilisi"] = TimeSpan.FromHours(4),
        ["Asia/Baku"] = TimeSpan.FromHours(4),
        ["Asia/Yerevan"] = TimeSpan.FromHours(4),
        ["Europe/Minsk"] = TimeSpan.FromHours(3),
        ["Europe/Kyiv"] = TimeSpan.FromHours(2),
        ["Europe/Kiev"] = TimeSpan.FromHours(2),

        // Мировые (без DST — фикс, лучше чем ничего)
        ["UTC"] = TimeSpan.Zero,
        ["Europe/Berlin"] = TimeSpan.FromHours(1),
        ["Europe/London"] = TimeSpan.Zero,
        ["Asia/Dubai"] = TimeSpan.FromHours(4),
        ["Asia/Shanghai"] = TimeSpan.FromHours(8),
        ["America/New_York"] = TimeSpan.FromHours(-5),
    };

    public static bool TryResolve(string id, out TimeZoneInfo tz)
    {
        tz = TimeZoneInfo.Utc;

        if (string.IsNullOrWhiteSpace(id))
            return false;

        // 1) Наш хардкод — приоритет, потому что гарантирует работу
        // даже если ICU неправильно сконфигурирован на Windows Server.
        if (FixedOffsets.TryGetValue(id, out var offset))
        {
            try
            {
                tz = TimeZoneInfo.CreateCustomTimeZone(
                    id,          // id
                    offset,      // baseUtcOffset
                    id,          // displayName
                    id);         // standardDisplayName

                return true;
            }
            catch { }
        }

        // 2) Прямой поиск — на Linux работает для IANA, на Windows для Windows-имён.
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException) { }
        catch (InvalidTimeZoneException) { }

        // 3) IANA → Windows
        try
        {
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var winId))
            {
                tz = TimeZoneInfo.FindSystemTimeZoneById(winId);
                return true;
            }
        }
        catch { }

        // 4) Windows → IANA
        try
        {
            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var ianaId))
            {
                tz = TimeZoneInfo.FindSystemTimeZoneById(ianaId);
                return true;
            }
        }
        catch { }

        return false;
    }

    public static string ToIana(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "UTC";
        if (id.Contains('/')) return id;

        try
        {
            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var ianaId))
                return ianaId;
        }
        catch { }

        return id;
    }
}