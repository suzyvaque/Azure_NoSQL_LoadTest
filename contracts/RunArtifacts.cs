using System.Globalization;

namespace Bmt.Contracts;

/// <summary>
/// The three backends under test. Used in run ids and metric labels.
/// </summary>
public enum Backend
{
    Mongo,
    CosmosRu,
    DocDb,
}

/// <summary>
/// Path and file-name conventions for <c>runs/&lt;run-id&gt;/</c> artifacts.
/// Run ids follow <c>YYYY-MM-DD_&lt;scenario&gt;_&lt;backend&gt;[_&lt;variant&gt;]</c>.
/// </summary>
public sealed class RunArtifacts
{
    /// <summary>Backend token used in run ids and labels.</summary>
    public static string BackendToken(Backend backend) => backend switch
    {
        Backend.Mongo => "mongo",
        Backend.CosmosRu => "cosmosru",
        Backend.DocDb => "docdb",
        _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, null),
    };

    /// <summary>Builds a run id from its parts.</summary>
    public static string BuildRunId(DateOnly date, string scenario, Backend backend, string? variant = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenario);
        string baseId = string.Create(
            CultureInfo.InvariantCulture,
            $"{date:yyyy-MM-dd}_{scenario}_{BackendToken(backend)}");
        return string.IsNullOrWhiteSpace(variant) ? baseId : $"{baseId}_{variant}";
    }

    public string RunsRoot { get; }
    public string RunId { get; }

    public RunArtifacts(string runsRoot, string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runsRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        RunsRoot = runsRoot;
        RunId = runId;
    }

    /// <summary>Directory holding all artifacts for this run.</summary>
    public string RunDir => Path.Combine(RunsRoot, RunId);

    /// <summary>Frozen effective configuration for the run.</summary>
    public string ConfigJson => Path.Combine(RunDir, "config.json");

    /// <summary>10&#160;% raw operation samples (<c>ts_us,op,latency_us,success,err</c>).</summary>
    public string RawSampleCsv => Path.Combine(RunDir, "raw-sample.csv");

    /// <summary>Structured load-generator log.</summary>
    public string LoadGenLog => Path.Combine(RunDir, "loadgen.log");

    /// <summary>Totals, error counts, run window, and host info.</summary>
    public string SummaryJson => Path.Combine(RunDir, "summary.json");

    /// <summary>Directory for rendered analysis charts.</summary>
    public string ChartsDir => Path.Combine(RunDir, "charts");

    /// <summary>Markdown analysis report.</summary>
    public string ReportMd => Path.Combine(RunDir, "report.md");

    /// <summary>Creates <see cref="RunDir"/> if it does not exist.</summary>
    public void EnsureRunDir() => Directory.CreateDirectory(RunDir);
}
