using System.Security.Cryptography;
using System.Text;
using S5.Recs.Models;

namespace S5.Recs.Infrastructure;

/// <summary>
/// Shared utilities for embedding text construction and content signature generation.
/// Used across partition-based pipeline workers for consistent hashing and caching.
/// Part of ARCH-0069: Partition-Based Import Pipeline Architecture.
/// </summary>
internal static class EmbeddingUtilities
{
    /// <summary>
    /// Builds embedding text from media entity.
    /// Format: Titles (distinct, joined by " / ") + Synopsis + Tags/Genres (comma-separated)
    /// This must be consistent with SeedService.BuildEmbeddingText for cache compatibility.
    /// </summary>
    public static string BuildEmbeddingText(Media media)
    {
        var titles = new List<string>();
        if (!string.IsNullOrWhiteSpace(media.Title))
            titles.Add(media.Title);
        if (!string.IsNullOrWhiteSpace(media.TitleEnglish) && media.TitleEnglish != media.Title)
            titles.Add(media.TitleEnglish!);
        if (!string.IsNullOrWhiteSpace(media.TitleRomaji) && media.TitleRomaji != media.Title)
            titles.Add(media.TitleRomaji!);
        if (!string.IsNullOrWhiteSpace(media.TitleNative) && media.TitleNative != media.Title)
            titles.Add(media.TitleNative!);
        if (media.Synonyms is { Length: > 0 })
            titles.AddRange(media.Synonyms);

        var tags = new List<string>();
        if (media.Genres is { Length: > 0 })
            tags.AddRange(media.Genres);
        if (media.Tags is { Length: > 0 })
            tags.AddRange(media.Tags);

        var text = $"{string.Join(" / ", titles.Distinct())}\n\n{media.Synopsis}\n\nTags: {string.Join(", ", tags.Distinct())}";
        return text.Trim();
    }

    /// <summary>
    /// Computes SHA256 content signature for embedding cache lookup.
    /// Same content = same signature = cache reuse.
    /// </summary>
    public static string ComputeContentSignature(Media media)
    {
        var text = BuildEmbeddingText(media);
        return ComputeContentSignature(text);
    }

    /// <summary>
    /// Computes SHA256 hash of text content.
    /// </summary>
    public static string ComputeContentSignature(string text)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
