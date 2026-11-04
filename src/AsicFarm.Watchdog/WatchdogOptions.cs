using System;
using System.Collections.Generic;

namespace AsicFarm.Watchdog;

public sealed class WatchdogOptions
{
    public const string SectionName = "Watchdog";

    public List<string> Hosts { get; set; } = new();
    public List<string> Subnets { get; set; } = new();

    public int RescanIntervalMinutes { get; set; } = 15;
    public string? Password { get; set; }
    public string? WhatsminerAdminPassword { get; set; }

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(20);

    public int MaxConcurrency { get; set; } = 16;
    public int ScanConcurrency { get; set; } = 32;
    public TimeSpan ScanTimeout { get; set; } = TimeSpan.FromMilliseconds(800);
    public bool VerboseSnapshotLog { get; set; }

    public string ServerUrl { get; set; } = "http://localhost:5000";
    public string ServerApiKey { get; set; } = "";
    public string WatchdogId { get; set; } = "watchdog-01";
}