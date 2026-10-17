namespace AsicFarm.Server.Data;

public sealed class MinerEntity
{
    public string Id { get; set; } = "";
    public string Ip { get; set; } = "";
    public string Mac { get; set; } = "";
    public string Hostname { get; set; } = "";
    public string Model { get; set; } = "";
    public string ModelCode { get; set; } = "";
    public string Algorithm { get; set; } = "";
    public string FirmwareName { get; set; } = "";
    public string FirmwareVersion { get; set; } = "";
    public string WatchdogId { get; set; } = "";
    public DateTimeOffset FirstSeen { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    public string LastSnapshotJson { get; set; } = "";
}

public sealed class WatchdogEntity
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTimeOffset FirstSeen { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    public int TotalSnapshotsReceived { get; set; }
}

public sealed class ScheduleEntity
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string TargetKind { get; set; } = "Miner";
    public string TargetId { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string TimeZoneId { get; set; } = "UTC";
    public string ActionsJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
}

public sealed class ScheduleRunEntity
{
    public long Id { get; set; }
    public long ScheduleId { get; set; }
    public DateTimeOffset RunAt { get; set; }
    public string Description { get; set; } = "";
    public int TotalTargets { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public string? Error { get; set; }
}

public sealed class AlertRuleEntity
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Target { get; set; } = "All";
    public double Threshold { get; set; }
    public string? Extra { get; set; }
    public int ForSeconds { get; set; } = 60;
    public int CooldownSeconds { get; set; } = 900;
    public bool Enabled { get; set; } = true;
    public string Channel { get; set; } = "Telegram";
    public string? ChannelConfig { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AlertEventEntity
{
    public long Id { get; set; }
    public long RuleId { get; set; }
    public string RuleName { get; set; } = "";
    public string MinerId { get; set; } = "";
    public string MinerIp { get; set; } = "";
    public DateTimeOffset FiredAt { get; set; }
    public string Message { get; set; } = "";
    public bool Delivered { get; set; }
    public string? DeliveryError { get; set; }
}

/// Override для appsettings.json. Что тут есть — побеждает. Что нет — берётся из IConfiguration.
public sealed class AppSettingEntity
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}

/// Правило авто-действия: если проблема держится N сек → выполни действие над майнером.
public sealed class AutoActionEntity
{
    public long Id { get; set; }
    public string Name { get; set; } = "";

    /// Триггер: "HashrateBelow" | "TempAbove" | "Offline".
    public string Trigger { get; set; } = "";

    /// Порог: для HashrateBelow — % (например 50), для TempAbove — °C, для Offline игнорируется.
    public double Threshold { get; set; }

    /// Как долго условие должно держаться.
    public int ForSeconds { get; set; } = 180;

    /// Действие: "Reboot" | "RestartMining" | "StopMining" | "PauseMining".
    public string Action { get; set; } = "";

    /// Цель: "All" | "Miner:<id>" | "Watchdog:<id>".
    public string Target { get; set; } = "All";

    /// Cooldown на майнер — не чаще одного действия за N секунд.
    public int CooldownSeconds { get; set; } = 1800;

    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastFiredAt { get; set; }
}

public sealed class AutoActionEventEntity
{
    public long Id { get; set; }
    public long RuleId { get; set; }
    public string RuleName { get; set; } = "";
    public string MinerId { get; set; } = "";
    public string MinerIp { get; set; } = "";
    public string Action { get; set; } = "";
    public DateTimeOffset FiredAt { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}