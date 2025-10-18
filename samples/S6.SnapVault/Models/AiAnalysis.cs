using System.Text.Json.Serialization;

namespace S6.SnapVault.Models;

/// <summary>
/// Structured AI analysis of a photo - optimized for display and search
/// </summary>
public class AiAnalysis
{
    /// <summary>
    /// Searchable keywords (6-10 tags, lowercase with hyphens for multi-word)
    /// Examples: "character", "studio", "graffiti", "cool-tones", "red-hoodie"
    /// </summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Clear, concise summary (20-50 words)
    /// Example: "A female character with dark brown hair and elf-like ears, wearing a black hoodie and red headphones, stands against a graffiti-style backdrop in a cool-toned, evenly lit studio."
    /// </summary>
    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    /// <summary>
    /// Structured facts about the photo, displayable as a table
    /// Core facts: Type, Composition, Palette, Lighting
    /// Optional contextual facts based on photo type
    /// </summary>
    [JsonPropertyName("facts")]
    public Dictionary<string, string> Facts { get; set; } = new();

    /// <summary>
    /// Fact keys that are locked and should be preserved during AI regeneration
    /// Used for "reroll with holds" mechanic - users can lock specific facts and reroll the rest
    /// </summary>
    [JsonPropertyName("lockedFactKeys")]
    public HashSet<string> LockedFactKeys { get; set; } = new();

    /// <summary>
    /// Converts analysis to embedding text for vector search
    /// Format: "tag1, tag2, tag3, summary sentence, fact1-value, fact2-value, ..."
    /// </summary>
    public string ToEmbeddingText()
    {
        var parts = new List<string>();

        // Add tags
        if (Tags.Count > 0)
        {
            parts.Add(string.Join(", ", Tags));
        }

        // Add summary
        if (!string.IsNullOrEmpty(Summary))
        {
            parts.Add(Summary);
        }

        // Add fact values
        if (Facts.Count > 0)
        {
            parts.AddRange(Facts.Values);
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Creates an error state analysis when parsing fails
    /// </summary>
    public static AiAnalysis CreateError(string message)
    {
        return new AiAnalysis
        {
            Tags = new List<string> { "error" },
            Summary = message,
            Facts = new Dictionary<string, string>
            {
                { "Status", "Error" },
                { "Message", message }
            }
        };
    }
}
