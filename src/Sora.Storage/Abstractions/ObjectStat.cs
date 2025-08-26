namespace Sora.Storage.Abstractions;

public sealed record ObjectStat(long? Length, string? ContentType, DateTimeOffset? LastModified, string? ETag);