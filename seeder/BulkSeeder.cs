using Bmt.Contracts;
using Bmt.Contracts.Models;
using MongoDB.Driver;

namespace Bmt.Seeder;

/// <summary>
/// Batched, resumable bulk insert of <see cref="CalcInputDoc"/> into <c>bmt_db.calc_input</c>.
/// Uses unordered inserts with transient-error retry and reports progress.
/// </summary>
public sealed class BulkSeeder
{
    private const int MaxRetries = 12;
    private const int MaxThrottleRetries = 200;
    private const int MaxBackoffMs = 5000;

    private readonly IMongoCollection<CalcInputDoc> _collection;
    private readonly InputDocFactory _factory;
    private readonly int _batchSize;
    private readonly int _progressEvery;

    public BulkSeeder(
        IMongoCollection<CalcInputDoc> collection,
        InputDocFactory factory,
        int batchSize = 1000,
        int progressEvery = 10_000)
    {
        _collection = collection ?? throw new ArgumentNullException(nameof(collection));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _batchSize = batchSize > 0 ? batchSize : throw new ArgumentOutOfRangeException(nameof(batchSize));
        _progressEvery = progressEvery > 0 ? progressEvery : 10_000;
    }

    /// <summary>
    /// Seeds the inclusive id range [<paramref name="fromId"/>, <paramref name="toId"/>].
    /// Returns the number of documents inserted and the total payload bytes written.
    /// </summary>
    public async Task<SeedResult> SeedRangeAsync(long fromId, long toId, CancellationToken ct)
    {
        if (toId < fromId)
        {
            return new SeedResult(0, 0);
        }

        var insertOptions = new InsertManyOptions { IsOrdered = false };
        var batch = new List<CalcInputDoc>(_batchSize);
        long inserted = 0;
        long totalBytes = 0;
        var start = DateTime.UtcNow;

        for (long id = fromId; id <= toId; id++)
        {
            ct.ThrowIfCancellationRequested();

            var doc = _factory.Create(id);
            totalBytes += doc.Input.Length;
            batch.Add(doc);

            if (batch.Count >= _batchSize)
            {
                await InsertWithRetryAsync(batch, insertOptions, ct).ConfigureAwait(false);
                inserted += batch.Count;
                batch.Clear();
                MaybeLogProgress(inserted, toId - fromId + 1, start);
            }
        }

        if (batch.Count > 0)
        {
            await InsertWithRetryAsync(batch, insertOptions, ct).ConfigureAwait(false);
            inserted += batch.Count;
            batch.Clear();
        }

        return new SeedResult(inserted, totalBytes);
    }

    private async Task InsertWithRetryAsync(
        IReadOnlyList<CalcInputDoc> batch, InsertManyOptions options, CancellationToken ct)
    {
        // Cosmos DB (RU) throttles with 16500/429s (and, while autoscale ramps or a scaling
        // operation is in flight, transient 13 "Insert error" storms); an unordered insertMany
        // then partially succeeds. Retry only the documents that failed for a transient reason,
        // and treat duplicate-key errors (11000) as already-inserted so the seed stays idempotent
        // and resumable. Throttle-class failures are expected and benign during a 1M-doc seed, so
        // they get a much larger retry budget than genuine transient errors.
        var pending = batch.ToList();

        int throttleAttempts = 0;
        int transientAttempts = 0;

        for (int round = 1; ; round++)
        {
            try
            {
                await _collection.InsertManyAsync(pending, options, ct).ConfigureAwait(false);
                return;
            }
            catch (MongoBulkWriteException<CalcInputDoc> bulk)
            {
                var retryableErrors = bulk.WriteErrors
                    .Where(e => e.Category != ServerErrorCategory.DuplicateKey && e.Code != 11000)
                    .ToList();

                if (retryableErrors.Count == 0)
                {
                    // All failures were duplicate-key: every doc is now present.
                    return;
                }

                bool throttleOnly = retryableErrors.All(e => IsThrottleCode(e.Code));
                if (throttleOnly)
                {
                    if (++throttleAttempts >= MaxThrottleRetries)
                    {
                        throw;
                    }
                }
                else if (++transientAttempts >= MaxRetries)
                {
                    throw;
                }

                pending = retryableErrors.Select(e => pending[e.Index]).ToList();
                int delayMs = Math.Min((int)(Math.Pow(2, Math.Min(round, 8)) * 100), MaxBackoffMs);
                Console.Error.WriteLine(
                    $"throttled insert (throttle {throttleAttempts}/{MaxThrottleRetries}, transient {transientAttempts}/{MaxRetries}): " +
                    $"{pending.Count} docs requeued; retrying in {delayMs} ms");
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
            catch (MongoException ex) when (IsTransient(ex) && transientAttempts < MaxRetries)
            {
                transientAttempts++;
                int delayMs = Math.Min((int)(Math.Pow(2, Math.Min(round, 8)) * 100), MaxBackoffMs);
                Console.Error.WriteLine(
                    $"transient insert error (attempt {transientAttempts}/{MaxRetries}): {ex.Message}; retrying in {delayMs} ms");
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
        }
    }

    private static bool IsThrottleCode(int code) =>
        // 16500 = Cosmos RU rate limit (RetryAfterMs); 429 = TooManyRequests;
        // 13 = transient "Insert error" seen during autoscale ramp / scaling operations;
        // 16 / 50 = transient command failures Cosmos surfaces under load.
        code is 16500 or 429 or 13 or 16 or 50;

    private static bool IsTransient(MongoException ex) => ex switch
    {
        MongoConnectionException => true,
        MongoExecutionTimeoutException => true,
        MongoCommandException cmd => cmd.Code is 16500 or 50 or 91 or 189 or 11600 or 11602,
        MongoWriteException we => we.WriteError?.Category == ServerErrorCategory.ExecutionTimeout,
        _ => false,
    };

    private void MaybeLogProgress(long inserted, long total, DateTime start)
    {
        if (inserted % _progressEvery != 0)
        {
            return;
        }

        var elapsed = DateTime.UtcNow - start;
        double rate = inserted / Math.Max(elapsed.TotalSeconds, 0.001);
        Console.WriteLine(
            $"  seeded {inserted:N0}/{total:N0} ({(double)inserted / total:P1}) " +
            $"at {rate:N0} docs/s, elapsed {elapsed:hh\\:mm\\:ss}");
    }
}

/// <summary>Result of a seed run: documents inserted and total <c>Input</c> payload bytes.</summary>
public readonly record struct SeedResult(long Inserted, long TotalBytes);
