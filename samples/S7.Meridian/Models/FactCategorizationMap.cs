using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Core;
using Koan.Data.Core.Model;

namespace Koan.Samples.Meridian.Models;

/// <summary>
/// Stage 2: Cached LLM-generated semantic grouping of facts into contextual batches.
/// CatalogHash is SHA-512 of sorted fact catalog to enable automatic cache invalidation on schema changes.
/// </summary>
public sealed class FactCategorizationMap : Entity<FactCategorizationMap>
{
    public string CatalogHash { get; set; } = string.Empty;
    public List<SemanticBatch> Batches { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Compute SHA-512 hash of fact catalog for cache key.
    /// </summary>
    public static string ComputeCatalogHash(FactCatalog catalog)
    {
        // Sort facts by FieldPath for consistent hashing
        var sortedFacts = catalog.Facts
            .OrderBy(f => f.FieldPath, StringComparer.Ordinal)
            .ToList();

        // Build canonical string representation
        var sb = new StringBuilder();
        foreach (var fact in sortedFacts)
        {
            sb.Append(fact.FieldPath);
            sb.Append('|');
            sb.Append(fact.Description);
            sb.Append('|');
            sb.Append(fact.Source);
            sb.Append('|');
            sb.Append(string.Join(",", fact.Examples.OrderBy(e => e, StringComparer.Ordinal)));
            sb.Append('\n');
        }

        // Compute SHA-512 hash
        using var sha512 = SHA512.Create();
        var hashBytes = sha512.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hashBytes);
    }

    /// <summary>
    /// Get existing categorization map by catalog hash, or return null if not cached.
    /// </summary>
    public static async Task<FactCategorizationMap?> GetByCatalogHashAsync(
        string catalogHash,
        CancellationToken ct = default)
    {
        var results = await Query(m => m.CatalogHash == catalogHash, ct);
        return results.FirstOrDefault();
    }

    /// <summary>
    /// Save this categorization map with the catalog hash.
    /// </summary>
    public static async Task<FactCategorizationMap> SaveWithHashAsync(
        string catalogHash,
        List<SemanticBatch> batches,
        CancellationToken ct = default)
    {
        var map = new FactCategorizationMap
        {
            CatalogHash = catalogHash,
            Batches = batches,
            CreatedAt = DateTime.UtcNow
        };

        return await map.Save(ct);
    }
}

/// <summary>
/// Semantic batch of related facts that benefit from shared context during extraction.
/// </summary>
public sealed class SemanticBatch
{
    /// <summary>Unique batch identifier (e.g., "identity_tracking", "security_encryption").</summary>
    public string BatchId { get; set; } = string.Empty;

    /// <summary>Human-readable category name (e.g., "Identity & Tracking").</summary>
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>Description explaining the semantic relationship between facts in this batch.</summary>
    public string CategoryDescription { get; set; } = string.Empty;

    /// <summary>Field paths included in this batch (e.g., ["$.servicenow_id", "$.architect"]).</summary>
    public List<string> FieldPaths { get; set; } = new();
}
