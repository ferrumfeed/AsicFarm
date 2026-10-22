namespace AsicFarm.Shared.Contracts;

public sealed record ExecuteCommandMessage(MinerCommand Command);
public sealed record CommandResultMessage(MinerCommandResult Result);

public sealed record SetTargetTempRequest(string MinerId, int TargetTemp);
public sealed record ConfigurePresetSwitcherRequest(string MinerId, bool Enabled, string MinPreset, string MaxPreset, int RiseTemp, int DecreaseTemp);

// Ответ от ватчдога с пресетами
public sealed record PresetsResponse(string RequestId, List<AsicFarm.Shared.Vnish.VnishAutotunePreset> Presets);