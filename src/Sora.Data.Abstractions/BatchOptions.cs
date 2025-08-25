namespace Sora.Data.Abstractions;

public sealed record BatchOptions(bool RequireAtomic = false, string? IdempotencyKey = null, int? MaxItems = null);