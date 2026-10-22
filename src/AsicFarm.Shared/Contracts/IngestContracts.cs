using AsicFarm.Shared.Domain;

namespace AsicFarm.Shared.Contracts;

public sealed record IngestBatchRequest(
    string WatchdogId,
    DateTimeOffset SentAt,
    IReadOnlyList<MinerSnapshot> Snapshots);

public sealed record IngestBatchResponse(
    int Accepted,
    int Rejected,
    string? ServerMessage);

// DTO для передачи детального снапшота с ватчдога на сервер
public sealed record DetailedIngestRequest(
    string RequestId,
    string MinerId,
    bool Success,
    string? Error,
    MinerDetailedSnapshot? Snapshot);