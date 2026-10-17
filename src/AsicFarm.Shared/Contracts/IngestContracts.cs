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