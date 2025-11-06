using System.Text;
using System.Text.RegularExpressions;
using Koan.Data.Vector.Abstractions.Partition;
using Guard = Koan.Core.Utilities.Guard;

namespace Koan.Data.Vector.Connector.Weaviate.Partition;

/// <summary>
/// Maps partition identifiers to Weaviate class names using per-partition class strategy.
/// Each partition gets its own class (e.g., "KoanDocument_project_a", "KoanDocument_project_b").
/// </summary>
/// <remarks>
/// Weaviate class naming constraints:
/// <list type="bullet">
/// <item><description>Maximum length: 256 characters</description></item>
/// <item><description>Must start with uppercase letter</description></item>
/// <item><description>Can contain: letters, digits, underscores</description></item>
/// <item><description>Case-sensitive</description></item>
/// </list>
/// </remarks>
internal sealed class WeaviatePartitionMapper : IVectorPartitionMapper
{
    private const int MaxClassNameLength = 256;
    private const string BasePrefix = "KoanDocument";

    /// <inheritdoc />
    public string MapStorageName<TEntity>(string partitionId) where TEntity : class
    {
        if (string.IsNullOrWhiteSpace(partitionId))
            throw new ArgumentException("Partition ID cannot be null or whitespace.", nameof(partitionId));

        var sanitized = SanitizePartitionId(partitionId);
        var className = $"{BasePrefix}_{sanitized}";

        // Enforce Weaviate max length constraint
        if (className.Length > MaxClassNameLength)
        {
            // Truncate partition suffix to fit, keeping prefix intact
            var maxSuffixLength = MaxClassNameLength - BasePrefix.Length - 1; // -1 for underscore
            sanitized = sanitized.Substring(0, Math.Min(sanitized.Length, maxSuffixLength));
            className = $"{BasePrefix}_{sanitized}";
        }

        return className;
    }

    /// <inheritdoc />
    public string SanitizePartitionId(string partitionId)
    {
        if (string.IsNullOrWhiteSpace(partitionId))
            throw new ArgumentException("Partition ID cannot be null or whitespace.", nameof(partitionId));

        // 1. Convert to lowercase for consistency
        var sanitized = partitionId.ToLowerInvariant();

        // 2. Replace invalid characters with underscores
        // Weaviate classes can only contain: letters, digits, underscores
        // Hyphens are NOT allowed in class names
        sanitized = Regex.Replace(sanitized, @"[^a-z0-9_]", "_");

        // 3. Remove leading/trailing underscores
        sanitized = sanitized.Trim('_');

        // 4. Collapse multiple consecutive underscores to single
        sanitized = Regex.Replace(sanitized, @"_+", "_");

        // 5. Handle empty result (edge case: all-special-chars input)
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            // Use hash as fallback for completely unsanitizable inputs
            var hash = Math.Abs(partitionId.GetHashCode());
            sanitized = $"partition_{hash}";
        }

        return sanitized;
    }
}
