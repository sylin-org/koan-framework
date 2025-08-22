using MongoDB.Bson.Serialization.Attributes;

namespace Sora.Data.Cqrs.Outbox.Mongo;

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