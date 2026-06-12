using Bmt.Contracts;
using Xunit;

namespace Bmt.Contracts.Tests;

public class IdsTests
{
    [Theory]
    [InlineData(1, "1")]
    [InlineData(1653, "1653")]
    [InlineData(1_000_000, "1000000")]
    public void Format_ProducesNonPaddedString(long id, string expected)
    {
        Assert.Equal(expected, Ids.Format(id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Format_RejectsNonPositiveIds(long id)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Ids.Format(id));
    }

    [Fact]
    public void Uniform_StaysInRange()
    {
        const long n = 1000;
        var sampler = Ids.CreateSampler(IdDistribution.Uniform, n, seed: 42);
        for (int i = 0; i < 100_000; i++)
        {
            long v = sampler.Next();
            Assert.InRange(v, 1, n);
        }
    }

    [Fact]
    public void Uniform_IsDeterministicForSameSeed()
    {
        var a = Ids.CreateSampler(IdDistribution.Uniform, 1_000_000, seed: 7);
        var b = Ids.CreateSampler(IdDistribution.Uniform, 1_000_000, seed: 7);
        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(a.Next(), b.Next());
        }
    }

    [Fact]
    public void Uniform_CoversFullRange()
    {
        const int n = 50;
        var sampler = Ids.CreateSampler(IdDistribution.Uniform, n, seed: 1);
        var seen = new HashSet<long>();
        for (int i = 0; i < 100_000; i++)
        {
            seen.Add(sampler.Next());
        }

        Assert.Contains(1L, seen);
        Assert.Contains((long)n, seen);
        Assert.Equal(n, seen.Count);
    }

    [Fact]
    public void Zipfian_StaysInRange()
    {
        const long n = 1_000_000;
        var sampler = Ids.CreateSampler(IdDistribution.Zipfian, n, zipfExponent: 1.1, seed: 123);
        for (int i = 0; i < 200_000; i++)
        {
            long v = sampler.Next();
            Assert.InRange(v, 1, n);
        }
    }

    [Fact]
    public void Zipfian_IsDeterministicForSameSeed()
    {
        var a = Ids.CreateSampler(IdDistribution.Zipfian, 1_000_000, 1.1, seed: 99);
        var b = Ids.CreateSampler(IdDistribution.Zipfian, 1_000_000, 1.1, seed: 99);
        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(a.Next(), b.Next());
        }
    }

    [Fact]
    public void Zipfian_FavoursLowIds()
    {
        const long n = 1_000_000;
        var sampler = Ids.CreateSampler(IdDistribution.Zipfian, n, zipfExponent: 1.1, seed: 2024);

        int lowDecile = 0;
        const int draws = 200_000;
        long threshold = n / 10;
        for (int i = 0; i < draws; i++)
        {
            if (sampler.Next() <= threshold)
            {
                lowDecile++;
            }
        }

        // With s=1.1 the lowest 10% of keys should dominate far beyond a uniform 10%.
        double fraction = (double)lowDecile / draws;
        Assert.True(fraction > 0.5, $"Expected heavy low-id skew, got {fraction:P1} in lowest decile.");
    }

    [Fact]
    public void Zipfian_RejectsTooLargeN()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ZipfianIdSampler((long)int.MaxValue + 1, 1.1, new Random(1)));
    }
}
