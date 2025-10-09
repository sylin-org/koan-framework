using Koan.Data.Core;
using S16.PantryPal.Models;
using System.Reflection;

namespace S16.PantryPal.Services;

/// <summary>
/// Lightweight semantic (vector) + lexical fallback search for PantryItems.
/// Mirrors S5.Recs pattern at a minimal scope: blend vector + text when available,
/// fall back to in-memory term filtering, finally degrade to returning recent items.
/// </summary>
public sealed class PantrySearchService : IPantrySearchService
{
    public async Task<(IReadOnlyList<PantryItem> items, bool degraded)> SearchAsync(string? query, int? topK, CancellationToken ct)
    {
        var size = topK.GetValueOrDefault(25);
        if (size <= 0) size = 25;
        if (size > 200) size = 200;

        // Normalize query
        if (string.IsNullOrWhiteSpace(query))
        {
            var baseline = await PantryItem.FirstPage(size, ct);
            return (baseline, false);
        }
        var text = query.Trim();
        var terms = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(t => t.Length > 1)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(8) // cap terms for simplicity
                        .ToArray();

        // Attempt vector search when vector provider is available
        // Vector path (compile-time) – safe now that Koan.Data.Vector is referenced
        if (terms.Length > 0)
        {
            try
            {
                if (Koan.Data.Vector.Vector<PantryItem>.IsAvailable)
                {
                    // Embedding intentionally omitted (noise). Provide real vector here when integrating an embedding service.
                    var embedding = Array.Empty<float>(); // provider may fall back to text-only scoring
                    var vr = await Koan.Data.Vector.Vector<PantryItem>.Search(vector: embedding, text: text, alpha: 0.5, topK: size, ct: ct);
                    var ordered = new List<PantryItem>();
                    foreach (var m in vr.Matches)
                    {
                        var entity = await PantryItem.Get(m.Id, ct);
                        if (entity != null) ordered.Add(entity);
                    }
                    if (ordered.Count > 0)
                        return (ordered, false);
                }
            }
            catch { /* degrade silently */ }
        }

        // Lexical fallback (in-memory). NOTE: acceptable for small MVP volumes.
        try
        {
            var all = await PantryItem.All(ct);
            var filtered = all.Where(p => MatchAny(p, terms))
                              .OrderByDescending(p => p.ExpiresAt)
                              .ThenBy(p => p.Name)
                              .Take(size)
                              .ToList();
            if (filtered.Count > 0)
                return (filtered, true); // degraded because vector/hybrid unavailable
        }
        catch
        {
            // ignore; proceed to final baseline
        }

        // Final baseline: just return first page
        var fallback = await PantryItem.FirstPage(size, ct);
        return (fallback, true);
    }

    private static bool MatchAny(PantryItem p, string[] terms)
    {
        if (terms.Length == 0) return true;
        var fields = new[] { p.Name ?? string.Empty, p.Category ?? string.Empty, p.Status ?? string.Empty };
        return terms.Any(t => fields.Any(f => f.Contains(t, StringComparison.OrdinalIgnoreCase)));
    }
}
