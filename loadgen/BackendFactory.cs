using Bmt.Contracts;
using Bmt.Contracts.Models;
using MongoDB.Driver;

namespace Bmt.LoadGen;

/// <summary>
/// Builds a single <see cref="IMongoClient"/> tuned identically for all three backends.
/// </summary>
public static class BackendFactory
{
    /// <summary>
    /// Creates a client with the benchmark driver settings: <c>w:1</c>, local reads from the
    /// primary, retryable reads/writes, a pool sized to the worker count, and fixed timeouts.
    /// Cosmos DB for MongoDB (RU) does not support retryable writes, so they are disabled there.
    /// </summary>
    public static IMongoClient Create(string connectionString, int poolSize, string backendToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var settings = MongoClientSettings.FromConnectionString(connectionString);

        settings.MaxConnectionPoolSize = Math.Max(poolSize, 1);
        settings.MinConnectionPoolSize = Math.Min(50, settings.MaxConnectionPoolSize);

        settings.WriteConcern = WriteConcern.W1;
        settings.ReadConcern = ReadConcern.Local;
        settings.ReadPreference = ReadPreference.Primary;

        settings.RetryReads = true;
        settings.RetryWrites = !string.Equals(
            backendToken, RunArtifacts.BackendToken(Backend.CosmosRu), StringComparison.OrdinalIgnoreCase);

        settings.ConnectTimeout = TimeSpan.FromSeconds(10);
        settings.SocketTimeout = TimeSpan.FromSeconds(30);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(15);
        settings.WaitQueueTimeout = TimeSpan.FromSeconds(10);

        settings.ApplicationName = "bmt-loadgen";

        return new MongoClient(settings);
    }

    /// <summary>Resolves the collections used by the workload.</summary>
    public static (IMongoCollection<CalcInputDoc> Input, IMongoCollection<CalcOutputDoc> Output)
        GetCollections(IMongoClient client)
    {
        var db = client.GetDatabase(Names.Database);
        return (
            db.GetCollection<CalcInputDoc>(Names.CalcInput),
            db.GetCollection<CalcOutputDoc>(Names.CalcOutput));
    }
}
