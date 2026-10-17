using AsicFarm.Shared.Domain;

namespace AsicFarm.Shared.Contracts;

public enum MinerCommandKind
{
    Reboot,
    StartMining,
    StopMining,
    RestartMining,
    PauseMining,
    ResumeMining,
    SwitchPool,

    /// Запрос детального снапшота. Сервер шлёт ватчдогу через SignalR,
    /// ватчдог собирает и присылает результат через HTTP POST на /api/v1/ingest/detailed.
    RequestDetailedSnapshot,
}

public sealed record MinerCommand(
    string RequestId,
    string MinerId,
    MinerCommandKind Kind,
    int? PoolId = null);

public sealed record MinerCommandResult(
    string RequestId,
    string MinerId,
    bool Success,
    string? Error,
    DateTimeOffset CompletedAt);

/// Ватчдог → сервер: результат сборки детального снапшота.
public sealed record DetailedIngestRequest(
    string RequestId,
    string MinerId,
    bool Success,
    string? Error,
    MinerDetailedSnapshot? Snapshot);