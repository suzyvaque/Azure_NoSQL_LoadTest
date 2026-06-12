namespace Bmt.Contracts;

/// <summary>
/// Database and collection name constants shared by every component.
/// </summary>
public static class Names
{
    /// <summary>Logical database name for the benchmark.</summary>
    public const string Database = "bmt_db";

    /// <summary>Read source collection. Seeded with 1,000,000 docs; queried by <c>_id</c>.</summary>
    public const string CalcInput = "calc_input";

    /// <summary>Write target collection. Each task inserts then removes one doc by <c>_id</c>.</summary>
    public const string CalcOutput = "calc_output";
}
