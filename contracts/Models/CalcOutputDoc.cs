using MongoDB.Bson.Serialization.Attributes;

namespace Bmt.Contracts.Models;

/// <summary>
/// A document in <c>bmt_db.calc_output</c>. Each task inserts one then removes it by <c>_id</c>.
/// </summary>
public sealed class CalcOutputDoc
{
    /// <summary>
    /// Sequential numeric request id as a non-padded string (e.g. <c>"1653"</c>). Primary key,
    /// equal to the originating calc_input document id.
    /// </summary>
    [BsonId]
    [BsonElement("_id")]
    public string Id { get; set; } = default!;

    /// <summary>
    /// Mirror of <see cref="Id"/>, stored for production-schema parity. Not separately indexed.
    /// </summary>
    [BsonElement("ReqId")]
    public string ReqId { get; set; } = default!;

    /// <summary>Base64 result payload produced by the pseudo-calculation.</summary>
    [BsonElement("Output")]
    public string Output { get; set; } = default!;

    [BsonElement("ExitCode")]
    public int ExitCode { get; set; }

    [BsonElement("CalculatorFileNm")]
    public string CalculatorFileNm { get; set; } = default!;

    [BsonElement("CalculatorVersion")]
    public string CalculatorVersion { get; set; } = default!;

    [BsonElement("CompletedAtUtc")]
    public DateTime CompletedAtUtc { get; set; }
}
