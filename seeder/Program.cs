using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bmt.Contracts;
using Bmt.Contracts.Models;
using MongoDB.Driver;

namespace Bmt.Seeder;

/// <summary>
/// CLI entry point. Seeds <c>bmt_db.calc_input</c> with sequential-id documents that follow the
/// production size distribution, against any Mongo-compatible backend.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var backendOption = new Option<Backend>(
            "--backend", "Target backend: Mongo | CosmosRu | DocDb.") { IsRequired = true };
        var connOption = new Option<string?>(
            "--conn", "Connection string. Falls back to the BMT_CONN environment variable.");
        var docsOption = new Option<long>(
            "--docs", () => 1_000_000, "Target document count.");
        var batchOption = new Option<int>(
            "--batch-size", () => 1000, "Insert batch size.");
        var seedOption = new Option<int?>(
            "--seed", "Optional RNG seed for reproducible payloads.");
        var forceOption = new Option<bool>(
            "--force", () => false, "Drop existing documents and reseed from scratch.");

        var root = new RootCommand("BMT calc_input seeder")
        {
            backendOption, connOption, docsOption, batchOption, seedOption, forceOption,
        };

        root.SetHandler(async (context) =>
        {
            var backend = context.ParseResult.GetValueForOption(backendOption);
            var conn = context.ParseResult.GetValueForOption(connOption);
            var docs = context.ParseResult.GetValueForOption(docsOption);
            var batchSize = context.ParseResult.GetValueForOption(batchOption);
            var seed = context.ParseResult.GetValueForOption(seedOption);
            var force = context.ParseResult.GetValueForOption(forceOption);
            var ct = context.GetCancellationToken();

            context.ExitCode = await RunAsync(backend, conn, docs, batchSize, seed, force, ct)
                .ConfigureAwait(false);
        });

        return await root.InvokeAsync(args).ConfigureAwait(false);
    }

    private static async Task<int> RunAsync(
        Backend backend, string? conn, long docs, int batchSize, int? seed, bool force, CancellationToken ct)
    {
        conn ??= Environment.GetEnvironmentVariable("BMT_CONN");
        if (string.IsNullOrWhiteSpace(conn))
        {
            Console.Error.WriteLine("ERROR: provide --conn or set BMT_CONN.");
            return 2;
        }

        if (docs < 1)
        {
            Console.Error.WriteLine("ERROR: --docs must be >= 1.");
            return 2;
        }

        var backendToken = RunArtifacts.BackendToken(backend);
        Console.WriteLine($"=== seeder : backend={backendToken} target={docs:N0} docs ===");

        var settings = MongoClientSettings.FromConnectionString(conn);
        // Cosmos DB for MongoDB (RU) does not support retryable writes; the seeder's own
        // transient-error retry loop handles throttling (429) instead.
        settings.RetryWrites = backend != Backend.CosmosRu;
        settings.RetryReads = true;
        var client = new MongoClient(settings);
        var database = client.GetDatabase(Names.Database);
        var collection = database.GetCollection<CalcInputDoc>(Names.CalcInput);

        // Read the current document count to support resume. Use the cheap "count" command
        // (EstimatedDocumentCount) rather than an aggregate-based CountDocuments: under heavy
        // Cosmos RU throttling (e.g. while a throughput migration is in flight) the aggregate
        // pipeline is rejected with 429/Substatus 3200, whereas the count command survives.
        // Retry a few times so a transient throttle on startup does not abort the whole seed.
        long existing = 0;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                existing = await collection.EstimatedDocumentCountAsync(cancellationToken: ct).ConfigureAwait(false);
                break;
            }
            catch (MongoException ex) when (attempt < 10)
            {
                int delayMs = Math.Min(attempt * 1000, 5000);
                Console.Error.WriteLine(
                    $"count read throttled (attempt {attempt}/10): {ex.Message.Split('\n')[0]}; retrying in {delayMs} ms");
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
        }
        Console.WriteLine($"existing documents in {Names.Database}.{Names.CalcInput}: {existing:N0}");

        long fromId;
        if (force)
        {
            Console.WriteLine("--force: deleting existing documents...");
            await collection.DeleteManyAsync(FilterDefinition<CalcInputDoc>.Empty, ct).ConfigureAwait(false);
            fromId = 1;
        }
        else if (existing >= docs)
        {
            Console.WriteLine($"already seeded ({existing:N0} >= {docs:N0}); nothing to do. Use --force to rewrite.");
            return 0;
        }
        else
        {
            // Resume: seed the remaining contiguous tail.
            fromId = existing + 1;
            if (existing > 0)
            {
                Console.WriteLine($"resuming from id {fromId:N0}.");
            }
        }

        var factory = new InputDocFactory(seed);
        var bulkSeeder = new BulkSeeder(collection, factory, batchSize);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        SeedResult result;
        try
        {
            result = await bulkSeeder.SeedRangeAsync(fromId, docs, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("seeding cancelled.");
            return 130;
        }
        sw.Stop();

        long finalCount = await collection.CountDocumentsAsync(FilterDefinition<CalcInputDoc>.Empty, cancellationToken: ct)
            .ConfigureAwait(false);

        var summary = new SeedSummary
        {
            Backend = backendToken,
            Database = Names.Database,
            Collection = Names.CalcInput,
            TargetDocs = docs,
            InsertedThisRun = result.Inserted,
            FinalCount = finalCount,
            TotalInputBytes = result.TotalBytes,
            BatchSize = batchSize,
            Seed = seed,
            DurationSeconds = Math.Round(sw.Elapsed.TotalSeconds, 3),
            SizeDistribution = factory.BucketCounts.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            CompletedAtUtc = DateTime.UtcNow,
        };

        WriteSummary(backendToken, summary);

        Console.WriteLine(
            $"DONE: inserted {result.Inserted:N0} docs this run; final count {finalCount:N0}; " +
            $"{result.TotalBytes / (1024.0 * 1024.0):N1} MiB payload; {sw.Elapsed:hh\\:mm\\:ss}.");
        return finalCount >= docs ? 0 : 1;
    }

    private static void WriteSummary(string backendToken, SeedSummary summary)
    {
        var targetDir = Path.Combine(Directory.GetCurrentDirectory(), "runs");
        Directory.CreateDirectory(targetDir);

        var path = Path.Combine(targetDir, $"{backendToken}-seed-summary.json");
        var json = JsonSerializer.Serialize(summary, SummaryJsonOptions);
        File.WriteAllText(path, json);
        Console.WriteLine($"summary written: {path}");
    }

    private static readonly JsonSerializerOptions SummaryJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>Seed run summary written to <c>seeder/runs/&lt;backend&gt;-seed-summary.json</c>.</summary>
public sealed class SeedSummary
{
    public string Backend { get; set; } = default!;
    public string Database { get; set; } = default!;
    public string Collection { get; set; } = default!;
    public long TargetDocs { get; set; }
    public long InsertedThisRun { get; set; }
    public long FinalCount { get; set; }
    public long TotalInputBytes { get; set; }
    public int BatchSize { get; set; }
    public int? Seed { get; set; }
    public double DurationSeconds { get; set; }
    public Dictionary<string, long> SizeDistribution { get; set; } = new();
    public DateTime CompletedAtUtc { get; set; }
}
