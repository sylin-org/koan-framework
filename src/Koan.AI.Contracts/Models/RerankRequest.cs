namespace Koan.AI.Contracts.Models;

/// <summary>Request for document reranking (query + documents → scored documents).</summary>
public sealed record RerankRequest
{
    /// <summary>The search query to rank against.</summary>
    public required string Query { get; init; }

    /// <summary>Documents to rerank.</summary>
    public required IReadOnlyList<string> Documents { get; init; }

    /// <summary>Model name (e.g., "BAAI/bge-reranker-large", "recommended:rerank").</summary>
    public string? Model { get; init; }

    /// <summary>Return only the top N results.</summary>
    public int? TopN { get; init; }

    /// <summary>Minimum relevance score threshold (0.0–1.0).</summary>
    public double? Threshold { get; init; }

    /// <summary>Injected by router.</summary>
    public string? InternalConnectionString { get; set; }

    /// <summary>Pass-through vendor options.</summary>
    public IDictionary<string, object>? VendorOptions { get; init; }
}

/// <summary>Response from reranking.</summary>
public sealed record RerankResponse
{
    /// <summary>Reranked documents with scores, ordered by relevance (highest first).</summary>
    public required IReadOnlyList<RankedDocument> Documents { get; init; }

    /// <summary>Model used.</summary>
    public string? Model { get; init; }
}

/// <summary>A document with its relevance score.</summary>
public sealed record RankedDocument
{
    /// <summary>Original index in the input documents list.</summary>
    public required int Index { get; init; }

    /// <summary>The document text.</summary>
    public required string Document { get; init; }

    /// <summary>Relevance score (higher = more relevant).</summary>
    public required double Score { get; init; }
}
