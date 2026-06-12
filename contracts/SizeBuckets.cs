namespace Bmt.Contracts;

/// <summary>
/// Document <c>Input</c> payload size buckets reproducing the production distribution:
/// 5&#160;KB&#160;@&#160;10&#160;%, 16&#160;KB&#160;@&#160;30&#160;%, 44&#160;KB&#160;@&#160;40&#160;%, 58&#160;KB&#160;@&#160;20&#160;%.
/// </summary>
public enum SizeBucket
{
    Kb5,
    Kb16,
    Kb44,
    Kb58,
}

/// <summary>
/// Payload size bucket definitions and a probability-weighted sampler.
/// </summary>
public static class SizeBuckets
{
    /// <summary>Ordered bucket table: (bucket, payload bytes, selection probability).</summary>
    public static readonly IReadOnlyList<(SizeBucket Bucket, int Bytes, double Probability)> Table = new[]
    {
        (SizeBucket.Kb5, 5 * 1024, 0.10),
        (SizeBucket.Kb16, 16 * 1024, 0.30),
        (SizeBucket.Kb44, 44 * 1024, 0.40),
        (SizeBucket.Kb58, 58 * 1024, 0.20),
    };

    /// <summary>Payload size in bytes for a bucket.</summary>
    public static int Bytes(SizeBucket bucket) => bucket switch
    {
        SizeBucket.Kb5 => 5 * 1024,
        SizeBucket.Kb16 => 16 * 1024,
        SizeBucket.Kb44 => 44 * 1024,
        SizeBucket.Kb58 => 58 * 1024,
        _ => throw new ArgumentOutOfRangeException(nameof(bucket), bucket, null),
    };

    /// <summary>Selection probability for a bucket.</summary>
    public static double Probability(SizeBucket bucket) => bucket switch
    {
        SizeBucket.Kb5 => 0.10,
        SizeBucket.Kb16 => 0.30,
        SizeBucket.Kb44 => 0.40,
        SizeBucket.Kb58 => 0.20,
        _ => throw new ArgumentOutOfRangeException(nameof(bucket), bucket, null),
    };

    /// <summary>
    /// Samples a bucket according to the probability table using the supplied RNG.
    /// </summary>
    public static SizeBucket Sample(Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);

        double u = rng.NextDouble();
        double cumulative = 0.0;
        foreach (var (bucket, _, probability) in Table)
        {
            cumulative += probability;
            if (u < cumulative)
            {
                return bucket;
            }
        }

        // Guard against floating-point drift on the final boundary.
        return Table[^1].Bucket;
    }
}
