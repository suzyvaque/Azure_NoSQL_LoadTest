using Bmt.Contracts;
using Xunit;

namespace Bmt.Contracts.Tests;

public class SizeBucketsTests
{
    [Fact]
    public void Probabilities_SumToOne()
    {
        double sum = 0;
        foreach (var (_, _, p) in SizeBuckets.Table)
        {
            sum += p;
        }

        Assert.Equal(1.0, sum, precision: 9);
    }

    [Theory]
    [InlineData(SizeBucket.Kb5, 5 * 1024)]
    [InlineData(SizeBucket.Kb16, 16 * 1024)]
    [InlineData(SizeBucket.Kb44, 44 * 1024)]
    [InlineData(SizeBucket.Kb58, 58 * 1024)]
    public void Bytes_MatchBucket(SizeBucket bucket, int expected)
    {
        Assert.Equal(expected, SizeBuckets.Bytes(bucket));
    }

    [Fact]
    public void Sample_ApproximatesTargetDistribution()
    {
        var rng = new Random(2024);
        var counts = new Dictionary<SizeBucket, int>();
        const int draws = 1_000_000;
        for (int i = 0; i < draws; i++)
        {
            var b = SizeBuckets.Sample(rng);
            counts[b] = counts.GetValueOrDefault(b) + 1;
        }

        AssertWithin(0.10, counts[SizeBucket.Kb5] / (double)draws);
        AssertWithin(0.30, counts[SizeBucket.Kb16] / (double)draws);
        AssertWithin(0.40, counts[SizeBucket.Kb44] / (double)draws);
        AssertWithin(0.20, counts[SizeBucket.Kb58] / (double)draws);
    }

    private static void AssertWithin(double expected, double actual)
    {
        Assert.True(Math.Abs(expected - actual) < 0.01,
            $"Expected ~{expected:P1}, got {actual:P1}.");
    }
}
