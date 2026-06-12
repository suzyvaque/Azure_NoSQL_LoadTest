using Bmt.Contracts;
using Xunit;

namespace Bmt.Contracts.Tests;

public class ScenarioConfigTests
{
    private const string S2Json = """
    {
      "name": "S2",
      "description": "Prod peak hour",
      "durationMinutes": 60,
      "jobsPerHour": 1099,
      "tasksPerJob": 441,
      "arrival": "poisson",
      "workers": 500,
      "poolSize": 500,
      "distribution": "uniform",
      "idRange": [1, 1000000],
      "thinkMsMin": 50,
      "thinkMsMax": 250,
      "writeConcern": "w1",
      "warmupMinutes": 5,
      "warmupFractionOfTarget": 0.10
    }
    """;

    [Fact]
    public void Parse_ReadsAllFields()
    {
        var s = ScenarioConfig.Parse(S2Json);

        Assert.Equal("S2", s.Name);
        Assert.Equal(60, s.DurationMinutes);
        Assert.Equal(1099, s.JobsPerHour);
        Assert.Equal(441, s.TasksPerJob);
        Assert.Equal(IdDistribution.Uniform, s.Distribution);
        Assert.Equal(1, s.IdMin);
        Assert.Equal(1_000_000, s.IdMax);
        Assert.Equal(50, s.ThinkMsMin);
        Assert.Equal(250, s.ThinkMsMax);
    }

    [Fact]
    public void Parse_ReadsZipfianDistribution()
    {
        var json = S2Json.Replace("\"uniform\"", "\"zipfian\"");
        var s = ScenarioConfig.Parse(json);
        Assert.Equal(IdDistribution.Zipfian, s.Distribution);
    }

    [Fact]
    public void Validate_RejectsBadIdRange()
    {
        var json = S2Json.Replace("[1, 1000000]", "[5, 2]");
        Assert.Throws<FormatException>(() => ScenarioConfig.Parse(json));
    }

    [Fact]
    public void Validate_RejectsBadThinkTimes()
    {
        var json = S2Json.Replace("\"thinkMsMax\": 250", "\"thinkMsMax\": 10");
        Assert.Throws<FormatException>(() => ScenarioConfig.Parse(json));
    }
}

public class RunArtifactsTests
{
    [Fact]
    public void BuildRunId_FollowsConvention()
    {
        var id = RunArtifacts.BuildRunId(new DateOnly(2026, 6, 13), "S2", Backend.DocDb);
        Assert.Equal("2026-06-13_S2_docdb", id);
    }

    [Fact]
    public void BuildRunId_AppendsVariant()
    {
        var id = RunArtifacts.BuildRunId(new DateOnly(2026, 6, 13), "S2", Backend.Mongo, "zipf");
        Assert.Equal("2026-06-13_S2_mongo_zipf", id);
    }

    [Fact]
    public void Paths_AreComposedUnderRunDir()
    {
        var a = new RunArtifacts("runs", "2026-06-13_S2_docdb");
        Assert.EndsWith(Path.Combine("2026-06-13_S2_docdb", "summary.json"), a.SummaryJson);
        Assert.EndsWith(Path.Combine("2026-06-13_S2_docdb", "raw-sample.csv"), a.RawSampleCsv);
        Assert.EndsWith(Path.Combine("2026-06-13_S2_docdb", "config.json"), a.ConfigJson);
    }
}
