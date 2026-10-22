using System.Text.Json.Serialization;

namespace AsicFarm.Shared.Vnish;

// ---------- auth ----------
public sealed record VnishUnlockBody([property: JsonPropertyName("pw")] string Password);
public sealed record VnishUnlockSuccess([property: JsonPropertyName("token")] string Token);
public sealed record VnishAuthCheck([property: JsonPropertyName("unlock_timeout")] long? UnlockTimeout);
public sealed record VnishErr([property: JsonPropertyName("err")] string Error);
public sealed record VnishRebootAfter([property: JsonPropertyName("after")] long After);
public sealed record VnishSwitchPoolBody([property: JsonPropertyName("pool_id")] int PoolId);

// ---------- settings ----------
public sealed record VnishSaveConfigResult(
    [property: JsonPropertyName("restart_required")] bool RestartRequired,
    [property: JsonPropertyName("reboot_required")] bool RebootRequired);

// ---------- info ----------
public sealed record VnishNetworkStatus(
    [property: JsonPropertyName("mac")] string Mac,
    [property: JsonPropertyName("ip")] string Ip,
    [property: JsonPropertyName("netmask")] string Netmask,
    [property: JsonPropertyName("gateway")] string Gateway,
    [property: JsonPropertyName("dns")] IReadOnlyList<string> Dns,
    [property: JsonPropertyName("hostname")] string Hostname,
    [property: JsonPropertyName("dhcp")] bool? Dhcp);

public sealed record VnishSystemInfo(
    [property: JsonPropertyName("os")] string Os,
    [property: JsonPropertyName("miner_name")] string MinerName,
    [property: JsonPropertyName("file_system_version")] string FileSystemVersion,
    [property: JsonPropertyName("network_status")] VnishNetworkStatus Network,
    [property: JsonPropertyName("uptime")] string Uptime,
    [property: JsonPropertyName("mem_total")] int MemTotal,
    [property: JsonPropertyName("mem_free")] int MemFree,
    [property: JsonPropertyName("mem_free_percent")] int MemFreePercent,
    [property: JsonPropertyName("mem_buf")] int MemBuf,
    [property: JsonPropertyName("mem_buf_percent")] int MemBufPercent);

public sealed record VnishInfo(
    [property: JsonPropertyName("miner")] string Miner,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("fw_name")] string FirmwareName,
    [property: JsonPropertyName("fw_version")] string FirmwareVersion,
    [property: JsonPropertyName("platform")] string Platform,
    [property: JsonPropertyName("install_type")] string InstallType,
    [property: JsonPropertyName("build_time")] string BuildTime,
    [property: JsonPropertyName("algorithm")] string Algorithm,
    [property: JsonPropertyName("hr_measure")] string HashrateMeasure,
    [property: JsonPropertyName("serial")] string Serial,
    [property: JsonPropertyName("system")] VnishSystemInfo System);

// ---------- status ----------
public sealed record VnishStatusPane(
    [property: JsonPropertyName("miner_state")] string MinerState,
    [property: JsonPropertyName("miner_state_time")] long MinerStateTime,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("failure_code")] int? FailureCode,
    [property: JsonPropertyName("restart_required")] bool RestartRequired,
    [property: JsonPropertyName("reboot_required")] bool RebootRequired,
    [property: JsonPropertyName("find_miner")] bool FindMiner,
    [property: JsonPropertyName("unlocked")] bool Unlocked,
    [property: JsonPropertyName("unlock_timeout")] long? UnlockTimeout,
    [property: JsonPropertyName("warranty")] string? Warranty);

// ---------- model ----------
public sealed record VnishTopology(
    [property: JsonPropertyName("num_rows")] int NumRows,
    [property: JsonPropertyName("num_cols")] int NumCols,
    [property: JsonPropertyName("chips")] IReadOnlyList<IReadOnlyList<int>> Chips);

public sealed record VnishModelInfoChain(
    [property: JsonPropertyName("chips_per_chain")] int ChipsPerChain,
    [property: JsonPropertyName("chips_per_domain")] int ChipsPerDomain,
    [property: JsonPropertyName("num_chains")] int NumChains,
    [property: JsonPropertyName("topology")] VnishTopology Topology);

public sealed record VnishFanMinCount(
    [property: JsonPropertyName("min")] int Min,
    [property: JsonPropertyName("max")] int Max,
    [property: JsonPropertyName("default")] int Default);

public sealed record VnishCoolingConsts(
    [property: JsonPropertyName("min_fan_pwm")] int MinFanPwm,
    [property: JsonPropertyName("min_target_temp")] int MinTargetTemp,
    [property: JsonPropertyName("max_target_temp")] int MaxTargetTemp,
    [property: JsonPropertyName("fan_min_count")] VnishFanMinCount? FanMinCount);

public sealed record VnishOverclock(
    [property: JsonPropertyName("max_voltage")] int MaxVoltage,
    [property: JsonPropertyName("min_voltage")] int MinVoltage,
    [property: JsonPropertyName("default_voltage")] int DefaultVoltage,
    [property: JsonPropertyName("max_freq")] int MaxFreq,
    [property: JsonPropertyName("min_freq")] int MinFreq,
    [property: JsonPropertyName("default_freq")] int DefaultFreq,
    [property: JsonPropertyName("warn_freq")] int WarnFreq,
    [property: JsonPropertyName("max_voltage_stock_psu")] int MaxVoltageStockPsu);

public sealed record VnishMinerModelInfo(
    [property: JsonPropertyName("full_name")] string FullName,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("algorithm")] string Algorithm,
    [property: JsonPropertyName("series")] string Series,
    [property: JsonPropertyName("platform")] string Platform,
    [property: JsonPropertyName("install_type")] string InstallType,
    [property: JsonPropertyName("hr_measure")] string HashrateMeasure,
    [property: JsonPropertyName("serial")] string Serial,
    [property: JsonPropertyName("chain")] VnishModelInfoChain Chain,
    [property: JsonPropertyName("cooling")] VnishCoolingConsts Cooling,
    [property: JsonPropertyName("overclock")] VnishOverclock Overclock);

// ---------- summary ----------
public sealed record VnishTempRange(
    [property: JsonPropertyName("min")] int Min,
    [property: JsonPropertyName("max")] int Max);

public sealed record VnishMinerStatus(
    [property: JsonPropertyName("miner_state")] string MinerState,
    [property: JsonPropertyName("miner_state_time")] long MinerStateTime,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("failure_code")] int? FailureCode);

public sealed record VnishChainChipStatuses(
    [property: JsonPropertyName("red")] int Red,
    [property: JsonPropertyName("orange")] int Orange,
    [property: JsonPropertyName("grey")] int Grey);

public sealed record VnishChainStatus(
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("description")] string? Description);

public sealed record VnishAntmChain(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("frequency")] double Frequency,
    [property: JsonPropertyName("voltage")] int Voltage,
    [property: JsonPropertyName("power_consumption")] int PowerConsumption,
    [property: JsonPropertyName("hashrate_ideal")] double HashrateIdeal,
    [property: JsonPropertyName("hashrate_rt")] double HashrateRt,
    [property: JsonPropertyName("hashrate_percentage")] double HashratePercentage,
    [property: JsonPropertyName("hr_error")] double HrError,
    [property: JsonPropertyName("hw_errors")] int HwErrors,
    [property: JsonPropertyName("pcb_temp")] VnishTempRange PcbTemp,
    [property: JsonPropertyName("chip_temp")] VnishTempRange ChipTemp,
    [property: JsonPropertyName("chip_statuses")] VnishChainChipStatuses ChipStatuses,
    [property: JsonPropertyName("status")] VnishChainStatus Status,
    [property: JsonPropertyName("inlet_water_temp")] int? InletWaterTemp,
    [property: JsonPropertyName("outlet_water_temp")] int? OutletWaterTemp);

public sealed record VnishPoolStats(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("user")] string User,
    [property: JsonPropertyName("pool_type")] string PoolType,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("asic_boost")] bool AsicBoost,
    [property: JsonPropertyName("diff")] string Diff,
    [property: JsonPropertyName("diffa")] double Diffa,
    [property: JsonPropertyName("accepted")] int Accepted,
    [property: JsonPropertyName("rejected")] int Rejected,
    [property: JsonPropertyName("stale")] int Stale,
    [property: JsonPropertyName("ls_diff")] double LsDiff,
    [property: JsonPropertyName("ls_time")] string LsTime,
    [property: JsonPropertyName("ping")] int Ping);

public sealed record VnishFan(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("rpm")] int Rpm,
    [property: JsonPropertyName("max_rpm")] int MaxRpm,
    [property: JsonPropertyName("status")] string Status);

public sealed record VnishFanModeHolder([property: JsonPropertyName("name")] string Name);
public sealed record VnishFanSettings([property: JsonPropertyName("mode")] VnishFanModeHolder Mode);

public sealed record VnishCooling(
    [property: JsonPropertyName("fan_num")] int FanNum,
    [property: JsonPropertyName("fan_duty")] int FanDuty,
    [property: JsonPropertyName("fans")] IReadOnlyList<VnishFan> Fans,
    [property: JsonPropertyName("settings")] VnishFanSettings Settings);

public sealed record VnishAntmPsuTemps(
    [property: JsonPropertyName("llc1_temp")] int? Llc1,
    [property: JsonPropertyName("llc2_temp")] int? Llc2,
    [property: JsonPropertyName("pfc_temp")] int? Pfc);

public sealed record VnishAntmPsuInfo(
    [property: JsonPropertyName("psu_power_metering")] bool? PsuPowerMetering,
    [property: JsonPropertyName("temps")] VnishAntmPsuTemps? Temps);

public sealed record VnishAntmMinerStats(
    [property: JsonPropertyName("miner_status")] VnishMinerStatus MinerStatus,
    [property: JsonPropertyName("miner_type")] string MinerType,
    [property: JsonPropertyName("hr_stock")] double HashrateStock,
    [property: JsonPropertyName("hr_average")] double HashrateAverage,
    [property: JsonPropertyName("hr_realtime")] double HashrateRealtime,
    [property: JsonPropertyName("hr_nominal")] double HashrateNominal,
    [property: JsonPropertyName("hr_error")] double HrError,
    [property: JsonPropertyName("pcb_temp")] VnishTempRange PcbTemp,
    [property: JsonPropertyName("chip_temp")] VnishTempRange ChipTemp,
    [property: JsonPropertyName("power_consumption")] int PowerConsumption,
    [property: JsonPropertyName("power_efficiency")] double PowerEfficiency,
    [property: JsonPropertyName("hw_errors_percent")] double HwErrorsPercent,
    [property: JsonPropertyName("hw_errors")] int HwErrors,
    [property: JsonPropertyName("devfee_percent")] double DevFeePercent,
    [property: JsonPropertyName("devfee")] double DevFee,
    [property: JsonPropertyName("pools")] IReadOnlyList<VnishPoolStats> Pools,
    [property: JsonPropertyName("cooling")] VnishCooling Cooling,
    [property: JsonPropertyName("chains")] IReadOnlyList<VnishAntmChain> Chains,
    [property: JsonPropertyName("found_blocks")] int FoundBlocks,
    [property: JsonPropertyName("best_share")] long BestShare,
    [property: JsonPropertyName("psu")] VnishAntmPsuInfo? Psu);

public sealed record VnishSummary(
    [property: JsonPropertyName("miner")] VnishAntmMinerStats? Miner);

// ---------- metrics ----------
public sealed record VnishMetricsData(
    [property: JsonPropertyName("hashrate")] double Hashrate,
    [property: JsonPropertyName("pcb_max_temp")] int PcbMaxTemp,
    [property: JsonPropertyName("chip_max_temp")] int ChipMaxTemp,
    [property: JsonPropertyName("fan_duty")] int FanDuty,
    [property: JsonPropertyName("power_consumption")] int PowerConsumption);

public sealed record VnishTimeRecordMetrics(
    [property: JsonPropertyName("time")] long Time,
    [property: JsonPropertyName("data")] VnishMetricsData Data);

public sealed record VnishMetricAnnotation(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("chain_id")] int? ChainId);

public sealed record VnishTimeRecordAnnotation(
    [property: JsonPropertyName("time")] long Time,
    [property: JsonPropertyName("data")] VnishMetricAnnotation Data);

public sealed record VnishMetricsReply(
    [property: JsonPropertyName("timezone")] string Timezone,
    [property: JsonPropertyName("metrics")] IReadOnlyList<VnishTimeRecordMetrics> Metrics,
    [property: JsonPropertyName("annotations")] IReadOnlyList<VnishTimeRecordAnnotation> Annotations);

public sealed record VnishPresetInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("pretty")] string Pretty,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("modded_psu_required")] bool ModdedPsuRequired);

public sealed record VnishPresetSwitcher(
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("top_preset")] string? TopPreset,
    [property: JsonPropertyName("min_preset")] string? MinPreset,
    [property: JsonPropertyName("rise_temp")] int? RiseTemp,
    [property: JsonPropertyName("decrease_temp")] int? DecreaseTemp,
    [property: JsonPropertyName("check_time")] int? CheckTime,
    [property: JsonPropertyName("autochange_top_preset")] bool AutochangeTopPreset,
    [property: JsonPropertyName("ignore_fan_speed")] bool IgnoreFanSpeed,
    [property: JsonPropertyName("power_delta")] int? PowerDelta);

public sealed record VnishPerfSummary(
    [property: JsonPropertyName("current_preset")] VnishPresetInfo? CurrentPreset,
    [property: JsonPropertyName("preset_switcher")] VnishPresetSwitcher? PresetSwitcher);

public sealed record VnishAutotunePreset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("pretty")] string Pretty,
    [property: JsonPropertyName("status")] string Status);