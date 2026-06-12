using System.Text.Json.Serialization;
using Bmt.Contracts;

namespace Bmt.LoadGen;

/// <summary>Frozen effective configuration written to <c>runs/&lt;run-id&gt;/config.json</c>.</summary>
public sealed class EffectiveConfig
{
    public string RunId { get; set; } = default!;
    public string Backend { get; set; } = default!;
    public string ScenarioName { get; set; } = default!;
    public string ScenarioFile { get; set; } = default!;
    public int Workers { get; set; }
    public int PoolSize { get; set; }
    public int DurationMinutes { get; set; }
    public int JobsPerHour { get; set; }
    public int? RampToJobsPerHour { get; set; }
    public int TasksPerJob { get; set; }
    public string Distribution { get; set; } = default!;
    public double ZipfExponent { get; set; }
    public long[] IdRange { get; set; } = { 1, 1_000_000 };
    public int ThinkMsMin { get; set; }
    public int ThinkMsMax { get; set; }
    public string WriteConcern { get; set; } = "w1";
    public int? Seed { get; set; }
    public int MetricsPort { get; set; }
    public string Host { get; set; } = default!;
    public int ProcessorCount { get; set; }
    public DateTime StartedAtUtc { get; set; }
}

/// <summary>Run summary written to <c>runs/&lt;run-id&gt;/summary.json</c>.</summary>
public sealed class RunSummary
{
    public string RunId { get; set; } = default!;
    public string Backend { get; set; } = default!;
    public string ScenarioName { get; set; } = default!;
    public DateTime StartedAtUtc { get; set; }
    public DateTime EndedAtUtc { get; set; }
    public double DurationSeconds { get; set; }

    public long TotalOps { get; set; }
    public long TotalErrors { get; set; }
    public double OpsPerSecond { get; set; }
    public double TasksPerSecond { get; set; }
    public double ErrorRate { get; set; }

    public CountersSnapshot Counters { get; set; } = new();

    public string Host { get; set; } = default!;
    public int ProcessorCount { get; set; }

    [JsonIgnore]
    public ScenarioConfig? Scenario { get; set; }
}
