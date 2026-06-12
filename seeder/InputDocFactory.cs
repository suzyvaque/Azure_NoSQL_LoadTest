using Bmt.Contracts;
using Bmt.Contracts.Models;

namespace Bmt.Seeder;

/// <summary>
/// Builds <see cref="CalcInputDoc"/> instances whose <c>Input</c> payload follows the
/// production size distribution (5/16/44/58&#160;KB @ 10/30/40/20&#160;%). Payloads are random
/// base64 so backend compression behaves realistically. Deterministic for a given seed.
/// </summary>
public sealed class InputDocFactory
{
    private static readonly string[] CalculatorFiles =
    {
        "PricingEngine.dll",
        "RiskModel.dll",
        "ValuationCalc.dll",
        "ScenarioRunner.dll",
    };

    private static readonly string[] ReqClasses = { "A", "B", "C", "D" };

    private readonly Random _rng;
    private readonly long[] _bucketCounts = new long[Enum.GetValues<SizeBucket>().Length];

    /// <summary>Creates a factory with an optional RNG seed for reproducible payloads.</summary>
    public InputDocFactory(int? seed = null)
    {
        _rng = seed is { } s ? new Random(s) : new Random();
    }

    /// <summary>Number of documents generated per size bucket so far.</summary>
    public IReadOnlyDictionary<SizeBucket, long> BucketCounts =>
        Enum.GetValues<SizeBucket>().ToDictionary(b => b, b => _bucketCounts[(int)b]);

    /// <summary>Builds one document for the given sequential request id.</summary>
    public CalcInputDoc Create(long id)
    {
        var idStr = Ids.Format(id);
        var bucket = SizeBuckets.Sample(_rng);
        _bucketCounts[(int)bucket]++;

        return new CalcInputDoc
        {
            Id = idStr,
            ReqId = idStr,
            CalculatorFileNm = CalculatorFiles[_rng.Next(CalculatorFiles.Length)],
            CalculatorVersion = $"{1 + _rng.Next(3)}.{_rng.Next(10)}.{_rng.Next(10)}",
            SkipCalculation = false,
            Input = RandomBase64(SizeBuckets.Bytes(bucket)),
            SuccessExitCodeList = new[] { 0 },
            ReqClass = ReqClasses[_rng.Next(ReqClasses.Length)],
        };
    }

    /// <summary>
    /// Produces a base64 string whose length is approximately <paramref name="targetChars"/>,
    /// so the stored <c>Input</c> field size matches the chosen bucket.
    /// </summary>
    private string RandomBase64(int targetChars)
    {
        // base64 encodes 3 raw bytes into 4 chars.
        int rawBytes = (int)(targetChars * 3L / 4L);
        var buffer = new byte[rawBytes];
        _rng.NextBytes(buffer);
        return Convert.ToBase64String(buffer);
    }
}
