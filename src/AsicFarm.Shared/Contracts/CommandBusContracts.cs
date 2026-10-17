namespace AsicFarm.Shared.Contracts;

/// Сервер → ватчдог: выполни команду над майнером.
public sealed record ExecuteCommandMessage(MinerCommand Command);

/// Ватчдог → сервер: результат выполнения.
public sealed record CommandResultMessage(MinerCommandResult Result);