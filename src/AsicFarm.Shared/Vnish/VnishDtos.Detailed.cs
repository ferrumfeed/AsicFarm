using System.Text.Json.Serialization;

namespace AsicFarm.Shared.Vnish;

// ===== /chains =====

public sealed record VnishCgChainStatus(
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("failure_message")] string? FailureMessage);

public sealed record VnishAntmChipSensor(
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("board")] int Board,
    [property: JsonPropertyName("chip")] int Chip,
    [property: JsonPropertyName("loc")] int Location);

public sealed record VnishAntmChip(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("hr")] double Hashrate,
    [property: JsonPropertyName("freq")] int Frequency,
    [property: JsonPropertyName("volt")] int Voltage,
    [property: JsonPropertyName("temp")] double Temp,
    [property: JsonPropertyName("errs")] int Errors,
    [property: JsonPropertyName("grade")] string Grade,
    [property: JsonPropertyName("throttled")] bool Throttled);

public sealed record VnishAntmChainChips(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("status")] VnishCgChainStatus Status,
    [property: JsonPropertyName("hr_realtime")] double HashrateRealtime,
    [property: JsonPropertyName("hr_nominal")] double HashrateNominal,
    [property: JsonPropertyName("freq")] double Frequency,
    [property: JsonPropertyName("sensors")] IReadOnlyList<VnishAntmChipSensor> Sensors,
    [property: JsonPropertyName("chips")] IReadOnlyList<VnishAntmChip> Chips);

// ===== /chains/factory-info =====

public sealed record VnishFactoryInfoChain(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("board_model")] string BoardModel,
    [property: JsonPropertyName("serial")] string Serial,
    [property: JsonPropertyName("chip_bin")] int ChipBin,
    [property: JsonPropertyName("freq")] int Frequency,
    [property: JsonPropertyName("volt")] int Voltage,
    [property: JsonPropertyName("hashrate")] double Hashrate);

public sealed record VnishFactoryInfoReply(
    [property: JsonPropertyName("chains")] IReadOnlyList<VnishFactoryInfoChain>? Chains,
    [property: JsonPropertyName("has_pics")] bool? HasPics,
    [property: JsonPropertyName("hr_stock")] double? HashrateStock,
    [property: JsonPropertyName("psu_model")] string? PsuModel,
    [property: JsonPropertyName("psu_serial")] string? PsuSerial);