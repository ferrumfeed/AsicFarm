namespace AsicFarm.Shared.Domain;

public enum MinerState
{
    Unknown, Mining, Initializing, Starting, AutoTuning,
    Restarting, ShuttingDown, Stopped, Failure
}

public enum ChainState
{
    Unknown, Initializing, Mining, Stopped, Failure, Disconnected, Disabled
}

public enum ChipGrade { Grey, Orange, Red }

public enum PoolState { Unknown, Offline, Working, Disabled, Active, Rejecting }

public enum PoolKind { UserPool, DevFee, Refund }

public enum FanState { Ok, Lost }

public enum FanControlMode { Auto, Manual, Immersion }

public enum FirmwareKind { Unknown, Vnish, Stock }