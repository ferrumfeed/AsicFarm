namespace AsicFarm.Shared.Contracts;

public enum ScheduleTargetKind
{
    Miner,
    Watchdog,
    All,
}

/// Действие для автоматизации.
public enum ScheduleAction
{
    None,
    StartMining,
    StopMining,
    RestartMining,
    Reboot,
}

/// Одно действие в расписании: в такой-то день, в такое-то время, сделать то-то.
public sealed record DayAction(
    DayOfWeek Day,
    string Time,             // "HH:mm"
    ScheduleAction Action);

public sealed record ScheduleDto(
    long Id,
    string Name,
    ScheduleTargetKind TargetKind,
    string TargetId,
    bool Enabled,
    string TimeZoneId,
    IReadOnlyList<DayAction> Actions,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastRunAt,
    string? NextEventDescription);

public sealed record ScheduleCreateRequest(
    string Name,
    ScheduleTargetKind TargetKind,
    string TargetId,
    bool Enabled,
    string TimeZoneId,
    IReadOnlyList<DayAction> Actions);

public sealed record ScheduleRunDto(
    long Id,
    DateTimeOffset RunAt,
    string Description,
    int TotalTargets,
    int Succeeded,
    int Failed,
    string? Error);