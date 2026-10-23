namespace AsicFarm.Shared.Contracts;

public sealed record ExecuteCommandMessage(MinerCommand Command);
public sealed record CommandResultMessage(MinerCommandResult Result);

public sealed record SetTargetTempRequest(string MinerId, int TargetTemp);

// НОВЫЕ DTO:
public sealed record SetCoolingModeRequest(string MinerId, string Mode, int FanDuty);
public sealed record SetQuietModeRequest(string MinerId, bool Enabled);
public sealed record SetRestartTempRequest(string MinerId, int Temp);
public sealed record SetStartupDelayRequest(string MinerId, int Seconds);