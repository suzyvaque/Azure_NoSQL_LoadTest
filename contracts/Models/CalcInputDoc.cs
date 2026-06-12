using MongoDB.Bson.Serialization.Attributes;

namespace Bmt.Contracts.Models;

/// <summary>
/// A document in <c>bmt_db.calc_input</c>. Seeded with 1,000,000 documents and read by
/// <c>find({_id})</c> at the start of every task. The <c>Input</c> payload follows the
/// production size distribution (see <see cref="SizeBuckets"/>).
/// </summary>
public sealed class CalcInputDoc
{
    /// <summary>
    /// Sequential numeric request id as a non-padded string (e.g. <c>"1653"</c>). Primary key
    /// and the value used by every operation (IDHACK); no secondary index is required.
    /// </summary>
    [BsonId]
    [BsonElement("_id")]
    public string Id { get; set; } = default!;

    /// <summary>
    /// Mirror of <see cref="Id"/>, stored for production-schema parity. Not separately indexed.
    /// </summary>
    [BsonElement("ReqId")]
    public string ReqId { get; set; } = default!;

    /// <summary>Name of the calculator file that produced this request.</summary>
    [BsonElement("CalculatorFileNm")]
    public string CalculatorFileNm { get; set; } = default!;

    /// <summary>Calculator version string.</summary>
    [BsonElement("CalculatorVersion")]
    public string CalculatorVersion { get; set; } = default!;

    /// <summary>Whether the calculation should be skipped for this request.</summary>
    [BsonElement("SkipCalculation")]
    public bool SkipCalculation { get; set; }

    /// <summary>
    /// Base64 input payload sized per the production distribution (5/16/44/58&#160;KB). Random
    /// content so backend compression behaves realistically.
    /// </summary>
    [BsonElement("Input")]
    public string Input { get; set; } = default!;

    /// <summary>Exit codes that indicate a successful calculation.</summary>
    [BsonElement("SuccessExitCodeList")]
    public int[] SuccessExitCodeList { get; set; } = Array.Empty<int>();

    /// <summary>Request classification bucket.</summary>
    [BsonElement("ReqClass")]
    public string ReqClass { get; set; } = default!;
}
