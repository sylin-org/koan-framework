namespace Sora.Data.Abstractions;

// Minimal paging options that can flow through repositories without depending on web-layer types
public sealed record DataQueryOptions(int? Page = null, int? PageSize = null);

// Optional: paging-aware base repository contract, enabling server-side pushdown

// Optional query capability: raw string query (e.g., SQL, JSON filter)

// Optional: paging-aware string-query contract

// Optional query capability: LINQ predicate

// Optional: paging-aware LINQ contract