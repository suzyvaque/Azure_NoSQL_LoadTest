using System.Globalization;

namespace Bmt.Contracts;

/// <summary>
/// Request-id selection distributions. The id range is the seeded key space [1, N].
/// </summary>
public enum IdDistribution
{
    Uniform,
    Zipfian,
}

/// <summary>
/// Produces request ids in the inclusive range [1, N].
/// </summary>
public interface IIdSampler
{
    /// <summary>Returns the next request id in [1, N].</summary>
    long Next();
}

/// <summary>
/// Sequential request-id helpers and key-space samplers.
///
/// The <c>_id</c> stored in Mongo is a sequential numeric <b>string</b>, non-padded
/// (e.g. <c>"1653"</c>), matching the BMT reference data. This value is also the request id;
/// all three operations key on <c>_id</c> (IDHACK), so no secondary index is required.
/// </summary>
public static class Ids
{
    /// <summary>
    /// Formats a numeric request id as the non-padded invariant-culture string used for <c>_id</c>.
    /// </summary>
    public static string Format(long id)
    {
        if (id < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, "Request ids start at 1.");
        }

        return id.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Creates a sampler over [1, n] for the given distribution.
    /// </summary>
    /// <param name="distribution">Uniform or Zipfian.</param>
    /// <param name="n">Upper bound of the key space (inclusive). Must be >= 1.</param>
    /// <param name="zipfExponent">Skew exponent for Zipfian (ignored for uniform). Typical 1.1.</param>
    /// <param name="seed">Optional RNG seed for reproducibility.</param>
    public static IIdSampler CreateSampler(IdDistribution distribution, long n, double zipfExponent = 1.1, int? seed = null)
    {
        var rng = seed is { } s ? new Random(s) : new Random();
        return distribution switch
        {
            IdDistribution.Uniform => new UniformIdSampler(n, rng),
            IdDistribution.Zipfian => new ZipfianIdSampler(n, zipfExponent, rng),
            _ => throw new ArgumentOutOfRangeException(nameof(distribution), distribution, null),
        };
    }
}

/// <summary>
/// Uniform sampler over [1, n].
/// </summary>
public sealed class UniformIdSampler : IIdSampler
{
    private readonly long _n;
    private readonly Random _rng;

    public UniformIdSampler(long n, Random rng)
    {
        if (n < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(n), n, "Key space must contain at least 1 id.");
        }

        _n = n;
        _rng = rng ?? throw new ArgumentNullException(nameof(rng));
    }

    public long Next() => _rng.NextInt64(1, _n + 1);
}

/// <summary>
/// Zipfian sampler over [1, n] using rejection-inversion
/// (Hörmann &amp; Derflinger, 1996). O(1) per draw, no large precomputed table.
/// Lower ids (ranks) are favoured; the <c>exponent</c> controls the skew.
/// </summary>
public sealed class ZipfianIdSampler : IIdSampler
{
    private const double TaylorThreshold = 1e-8;
    private const double F12 = 0.5;
    private const double F13 = 1.0 / 3.0;
    private const double F14 = 0.25;

    private readonly int _n;
    private readonly double _exponent;
    private readonly Random _rng;
    private readonly double _hIntegralX1;
    private readonly double _hIntegralN;
    private readonly double _s;

    public ZipfianIdSampler(long n, double exponent, Random rng)
    {
        if (n < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(n), n, "Key space must contain at least 1 id.");
        }

        if (n > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(n), n, "Zipfian sampler supports up to int.MaxValue ids.");
        }

        if (exponent <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exponent), exponent, "Exponent must be positive.");
        }

        _n = (int)n;
        _exponent = exponent;
        _rng = rng ?? throw new ArgumentNullException(nameof(rng));

        _hIntegralX1 = HIntegral(1.5) - 1.0;
        _hIntegralN = HIntegral(_n + 0.5);
        _s = 2.0 - HIntegralInverse(HIntegral(2.5) - H(2));
    }

    public long Next()
    {
        while (true)
        {
            double u = _hIntegralN + _rng.NextDouble() * (_hIntegralX1 - _hIntegralN);
            double x = HIntegralInverse(u);
            int k = (int)(x + 0.5);

            if (k < 1)
            {
                k = 1;
            }
            else if (k > _n)
            {
                k = _n;
            }

            if (k - x <= _s || u >= HIntegral(k + 0.5) - H(k))
            {
                return k;
            }
        }
    }

    private double HIntegral(double x)
    {
        double logX = Math.Log(x);
        return Helper2((1.0 - _exponent) * logX) * logX;
    }

    private double H(double x) => Math.Exp(-_exponent * Math.Log(x));

    private double HIntegralInverse(double x)
    {
        double t = x * (1.0 - _exponent);
        if (t < -1.0)
        {
            t = -1.0;
        }

        return Math.Exp(Helper1(t) * x);
    }

    // helper1(x) = log(1+x)/x, accurate near 0.
    private static double Helper1(double x)
    {
        if (Math.Abs(x) > TaylorThreshold)
        {
            return Math.Log(1.0 + x) / x;
        }

        return 1.0 - x * (F12 - x * (F13 - F14 * x));
    }

    // helper2(x) = (exp(x)-1)/x, accurate near 0.
    private static double Helper2(double x)
    {
        if (Math.Abs(x) > TaylorThreshold)
        {
            return (Math.Exp(x) - 1.0) / x;
        }

        return 1.0 + x * F12 * (1.0 + x * F13 * (1.0 + F14 * x));
    }
}
