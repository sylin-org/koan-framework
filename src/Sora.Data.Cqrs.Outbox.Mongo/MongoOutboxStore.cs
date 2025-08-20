using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Sora.Data.Cqrs;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sora.Data.Cqrs.Outbox.Mongo;

public sealed class MongoOutboxOptions
{
    public string? ConnectionString { get; set; }
    public string? ConnectionStringName { get; set; } = "mongo";
    public string Database { get; set; } = "sora";
    public string Collection { get; set; } = "Outbox";
    public int MaxAttempts { get; set; } = 10;
    public int LeaseSeconds { get; set; } = 30;
}

internal sealed class MongoOutboxRecord
{
    [BsonId]
    public string Id { get; set; } = default!;
    public DateTimeOffset OccurredAt { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public Dictionary<string, string>? Headers { get; set; }
    public string? PartitionKey { get; set; }
    public string? DedupKey { get; set; }
    public int Attempt { get; set; }
    public DateTimeOffset VisibleAt { get; set; }
    public string? LeaseId { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public string Status { get; set; } = "Pending"; // Pending, Done, Dead
    public string? DeadReason { get; set; }
}

public sealed class MongoOutboxStore : IOutboxStore
{
    private readonly ILogger<MongoOutboxStore> _logger;
    private readonly IMongoCollection<MongoOutboxRecord> _col;
    private readonly MongoOutboxOptions _opts;

    public MongoOutboxStore(ILogger<MongoOutboxStore> logger, IOptions<MongoOutboxOptions> options, IConfiguration cfg)
    {
        _logger = logger; _opts = options.Value;
        var name = string.IsNullOrWhiteSpace(_opts.ConnectionStringName) ? "mongo" : _opts.ConnectionStringName!;
        var cs = OutboxConfig.ResolveConnectionString(cfg, provider: "mongo", inline: _opts.ConnectionString, name: name, defaultName: "mongo");
        if (string.IsNullOrWhiteSpace(cs)) throw new InvalidOperationException("Mongo outbox requires a ConnectionString or ConnectionStringName.");
        var client = new MongoClient(cs);
        var db = client.GetDatabase(_opts.Database);
        _col = db.GetCollection<MongoOutboxRecord>(_opts.Collection);
        EnsureIndexes(_col);
    }

    private static void EnsureIndexes(IMongoCollection<MongoOutboxRecord> c)
    {
        var idx1 = new CreateIndexModel<MongoOutboxRecord>(Builders<MongoOutboxRecord>.IndexKeys.Ascending(x => x.Status).Ascending(x => x.VisibleAt));
        var idx2 = new CreateIndexModel<MongoOutboxRecord>(Builders<MongoOutboxRecord>.IndexKeys.Ascending(x => x.LeaseUntil));
        var idx3 = new CreateIndexModel<MongoOutboxRecord>(Builders<MongoOutboxRecord>.IndexKeys.Ascending(x => x.DedupKey), new CreateIndexOptions { Unique = true, Sparse = true });
        c.Indexes.CreateMany(new[] { idx1, idx2, idx3 });
    }

    public async Task AppendAsync(OutboxEntry entry, CancellationToken ct = default)
    {
        var rec = ToRecord(entry);
        await _col.InsertOneAsync(rec, cancellationToken: ct);
    }

    public async Task<IReadOnlyList<OutboxEntry>> DequeueAsync(int max = 100, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var leaseId = Guid.NewGuid().ToString("n");
        var leaseUntil = now.AddSeconds(_opts.LeaseSeconds);
        var filter = Builders<MongoOutboxRecord>.Filter.And(
            Builders<MongoOutboxRecord>.Filter.Eq(x => x.Status, "Pending"),
            Builders<MongoOutboxRecord>.Filter.Lte(x => x.VisibleAt, now),
            Builders<MongoOutboxRecord>.Filter.Or(
                Builders<MongoOutboxRecord>.Filter.Eq(x => x.LeaseUntil, null),
                Builders<MongoOutboxRecord>.Filter.Lt(x => x.LeaseUntil, now)
            )
        );

        var update = Builders<MongoOutboxRecord>.Update
            .Set(x => x.LeaseId, leaseId)
            .Set(x => x.LeaseUntil, leaseUntil)
            .Inc(x => x.Attempt, 1);

        var found = await _col.Find(filter).Limit(max).ToListAsync(ct);
        var leased = new List<OutboxEntry>(found.Count);
        foreach (var f in found)
        {
            var res = await _col.UpdateOneAsync(
                Builders<MongoOutboxRecord>.Filter.And(
                    Builders<MongoOutboxRecord>.Filter.Eq(x => x.Id, f.Id),
                    Builders<MongoOutboxRecord>.Filter.Or(
                        Builders<MongoOutboxRecord>.Filter.Eq(x => x.LeaseUntil, null),
                        Builders<MongoOutboxRecord>.Filter.Lt(x => x.LeaseUntil, now)
                    )
                ), update, options: null, cancellationToken: ct);
            if (res.ModifiedCount == 1)
            {
                f.LeaseId = leaseId; f.LeaseUntil = leaseUntil;
                leased.Add(ToEntry(f));
            }
        }
        return leased;
    }

    public async Task MarkProcessedAsync(string id, CancellationToken ct = default)
    {
        await _col.UpdateOneAsync(
            Builders<MongoOutboxRecord>.Filter.Eq(x => x.Id, id),
            Builders<MongoOutboxRecord>.Update.Set(x => x.Status, "Done").Unset(x => x.LeaseId).Unset(x => x.LeaseUntil),
            options: null,
            cancellationToken: ct
        );
    }

    private static MongoOutboxRecord ToRecord(OutboxEntry e) => new()
    {
        Id = e.Id,
        OccurredAt = e.OccurredAt,
        EntityType = e.EntityType,
        Operation = e.Operation,
        EntityId = e.EntityId,
        PayloadJson = e.PayloadJson,
        Headers = null,
        PartitionKey = e.EntityType,
        DedupKey = null,
        Attempt = 0,
        VisibleAt = DateTimeOffset.UtcNow,
        LeaseId = null,
        LeaseUntil = null,
        Status = "Pending",
    };

    private static OutboxEntry ToEntry(MongoOutboxRecord r) => new(
        r.Id, r.OccurredAt, r.EntityType, r.Operation, r.EntityId, r.PayloadJson
    );
}

public static class MongoOutboxRegistration
{
    public static IServiceCollection AddMongoOutbox(this IServiceCollection services, Action<MongoOutboxOptions>? configure = null)
    {
        services.BindOutboxOptions<MongoOutboxOptions>("Mongo");
        if (configure is not null) services.PostConfigure(configure);
        services.AddSingleton<IOutboxStore, MongoOutboxStore>();
        services.AddSingleton<IOutboxStoreFactory, MongoOutboxFactory>();
        return services;
    }
}

// Auto-registration for discovery
// legacy initializer removed in favor of standardized auto-registrar

[Sora.Data.Abstractions.ProviderPriority(20)]
public sealed class MongoOutboxFactory : IOutboxStoreFactory
{
    public string Provider => "mongo";
    public IOutboxStore Create(IServiceProvider sp) => sp.GetRequiredService<MongoOutboxStore>();
}
