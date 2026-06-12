using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bmt.Contracts;

/// <summary>
/// Deserialization model for files in <c>scenarios/</c>. Mirrors the JSON schema documented
/// in <c>scenarios/README.md</c>.
/// </summary>
public sealed class ScenarioConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = default!;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("durationMinutes")]
    public int DurationMinutes { get; set; }

    [JsonPropertyName("jobsPerHour")]
    public int JobsPerHour { get; set; }

    [JsonPropertyName("tasksPerJob")]
    public int TasksPerJob { get; set; }

    [JsonPropertyName("arrival")]
    public string Arrival { get; set; } = "poisson";

    [JsonPropertyName("workers")]
    public int Workers { get; set; }

    [JsonPropertyName("poolSize")]
    public int PoolSize { get; set; }

    [JsonPropertyName("distribution")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public IdDistribution Distribution { get; set; } = IdDistribution.Uniform;

    /// <summary>Zipfian skew exponent; only used when <see cref="Distribution"/> is Zipfian.</summary>
    [JsonPropertyName("zipfExponent")]
    public double ZipfExponent { get; set; } = 1.1;

    /// <summary>Inclusive id range [min, max] over <c>calc_input</c> keys.</summary>
    [JsonPropertyName("idRange")]
    public long[] IdRange { get; set; } = { 1, 1_000_000 };

    [JsonPropertyName("thinkMsMin")]
    public int ThinkMsMin { get; set; }

    [JsonPropertyName("thinkMsMax")]
    public int ThinkMsMax { get; set; }

    [JsonPropertyName("writeConcern")]
    public string WriteConcern { get; set; } = "w1";

    [JsonPropertyName("warmupMinutes")]
    public int WarmupMinutes { get; set; }

    [JsonPropertyName("warmupFractionOfTarget")]
    public double WarmupFractionOfTarget { get; set; }

    /// <summary>Lower bound of the id range.</summary>
    [JsonIgnore]
    public long IdMin => IdRange is { Length: > 0 } ? IdRange[0] : 1;

    /// <summary>Upper bound of the id range.</summary>
    [JsonIgnore]
    public long IdMax => IdRange is { Length: > 1 } ? IdRange[1] : 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Deserializes a scenario from a JSON string.</summary>
    public static ScenarioConfig Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var config = JsonSerializer.Deserialize<ScenarioConfig>(json, SerializerOptions)
                     ?? throw new FormatException("Scenario JSON deserialized to null.");
        config.Validate();
        return config;
    }

    /// <summary>Loads and validates a scenario from a file path.</summary>
    public static ScenarioConfig Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(File.ReadAllText(path));
    }

    /// <summary>Throws <see cref="FormatException"/> if the scenario is internally inconsistent.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new FormatException("Scenario 'name' is required.");
        }

        if (IdRange is not { Length: 2 } || IdRange[0] < 1 || IdRange[1] < IdRange[0])
        {
            throw new FormatException("Scenario 'idRange' must be [min, max] with 1 <= min <= max.");
        }

        if (ThinkMsMin < 0 || ThinkMsMax < ThinkMsMin)
        {
            throw new FormatException("Scenario think time must satisfy 0 <= thinkMsMin <= thinkMsMax.");
        }

        if (Workers < 1 || PoolSize < 1)
        {
            throw new FormatException("Scenario 'workers' and 'poolSize' must be >= 1.");
        }
    }
}
