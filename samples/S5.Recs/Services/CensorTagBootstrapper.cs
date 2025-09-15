using S5.Recs.Models;
using Koan.Data.Core;
using System.Text.RegularExpressions;

namespace S5.Recs.Services;

public static class CensorTagBootstrapper
{
    // Example: simple profanity list (expand as needed)
    private static readonly string[] Profanity = new[] { "sex", "violence", "drugs", "nsfw", "explicit", "gore", "hentai", "ecchi", "yaoi", "yuri", "smut", "18+", "adult" };

    public static async Task<List<string>> GetCandidateCensoredTags(CancellationToken ct)
    {
        var allDocs = await Media.All(ct);
        var allTags = allDocs.SelectMany(m => m.Tags ?? Array.Empty<string>())
            .Concat(allDocs.SelectMany(m => m.Genres ?? Array.Empty<string>()))
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct()
            .ToList();
        // Heuristic: match profanity or banned patterns
        var candidates = allTags.Where(IsCensoredTag).Distinct().OrderBy(t => t).ToList();
        return candidates;
    }

    private static bool IsCensoredTag(string tag)
    {
        // Simple: match against profanity list or regex
        if (Profanity.Contains(tag)) return true;
        // Example: regex for explicit/banned patterns
        if (Regex.IsMatch(tag, @"(nsfw|explicit|18\+|smut|hentai|gore|violence|drugs)", RegexOptions.IgnoreCase)) return true;
        return false;
    }

    public static async Task EnsureCensorTagsPopulated(CancellationToken ct)
    {
        var doc = await CensorTagsDoc.Get("recs:censor-tags", ct);
        if (doc is null || doc.Tags is null || doc.Tags.Count == 0)
        {
            var candidates = await GetCandidateCensoredTags(ct);
            if (doc is null) doc = new CensorTagsDoc { Id = "recs:censor-tags" };
            doc.Tags = candidates;
            doc.UpdatedAt = DateTimeOffset.UtcNow;
            await CensorTagsDoc.UpsertMany(new[] { doc }, ct);
        }
    }
}
