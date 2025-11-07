using System.Text;
using System.Text.RegularExpressions;
using Koan.Data.Vector.Abstractions.Partition;
using Microsoft.Extensions.Logging;

namespace Koan.Data.Vector.Connector.Weaviate.Partition;

/// <summary>
/// Weaviate-specific partition mapping implementation
/// </summary>
/// <remarks>
/// Weaviate class naming requirements:
/// - Must start with uppercase letter
/// - Can contain: letters, numbers, underscores
/// - Max length: 256 characters
/// - Convention: PascalCase with underscores for separators
///
/// This mapper creates per-class-per-partition storage:
/// - Base pattern: "Koan{EntityName}_{sanitizedPartitionId}"
/// - Example: "KoanDocumentChunk_project_abc123"
/// </remarks>
public partial class WeaviatePartitionMapper : IVectorPartitionMapper
{
    private readonly ILogger<WeaviatePartitionMapper> _logger;
    private const int MaxClassNameLength = 256;
    private const string ClassPrefix = "Koan";

    public WeaviatePartitionMapper(ILogger<WeaviatePartitionMapper> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public string MapStorageName<T>(string partitionId)
    {
        if (string.IsNullOrWhiteSpace(partitionId))
        {
            throw new ArgumentException("Partition ID cannot be null or whitespace", nameof(partitionId));
        }

        var baseName = GetBaseName<T>();
        var sanitizedId = SanitizePartitionId(partitionId);

        var className = $"{baseName}_{sanitizedId}";

        // Ensure max length
        if (className.Length > MaxClassNameLength)
        {
            var maxIdLength = MaxClassNameLength - baseName.Length - 1; // -1 for underscore
            sanitizedId = sanitizedId.Substring(0, Math.Max(1, maxIdLength));
            className = $"{baseName}_{sanitizedId}";

            _logger.LogWarning(
                "Class name truncated to {MaxLength} chars: {ClassName}",
                MaxClassNameLength,
                className);
        }

        return className;
    }

    /// <inheritdoc/>
    public string SanitizePartitionId(string partitionId)
    {
        if (string.IsNullOrWhiteSpace(partitionId))
        {
            return "default";
        }

        // Replace invalid characters with underscores
        var sanitized = InvalidCharRegex().Replace(partitionId, "_");

        // Remove leading/trailing underscores
        sanitized = sanitized.Trim('_');

        // Collapse multiple consecutive underscores
        sanitized = MultipleUnderscoresRegex().Replace(sanitized, "_");

        // Ensure lowercase (Weaviate is case-sensitive)
        sanitized = sanitized.ToLowerInvariant();

        // Ensure non-empty
        if (string.IsNullOrEmpty(sanitized))
        {
            sanitized = "default";
        }

        return sanitized;
    }

    /// <inheritdoc/>
    public string GetBaseName<T>()
    {
        var entityType = typeof(T);
        var typeName = entityType.Name;

        // Remove generic type markers (e.g., Entity`1)
        var backtickIndex = typeName.IndexOf('`');
        if (backtickIndex > 0)
        {
            typeName = typeName.Substring(0, backtickIndex);
        }

        // Ensure PascalCase with Koan prefix
        var baseName = $"{ClassPrefix}{typeName}";

        // Sanitize to valid Weaviate class name
        baseName = InvalidCharRegex().Replace(baseName, string.Empty);

        // Ensure starts with uppercase
        if (baseName.Length > 0 && char.IsLower(baseName[0]))
        {
            baseName = char.ToUpper(baseName[0]) + baseName.Substring(1);
        }

        return baseName;
    }

    /// <summary>
    /// Regex for invalid Weaviate class name characters
    /// </summary>
    /// <remarks>
    /// Weaviate allows: letters, numbers, underscores
    /// This regex matches anything else (including hyphens)
    /// </remarks>
    [GeneratedRegex(@"[^a-zA-Z0-9_]")]
    private static partial Regex InvalidCharRegex();

    /// <summary>
    /// Regex for multiple consecutive underscores
    /// </summary>
    [GeneratedRegex(@"_+")]
    private static partial Regex MultipleUnderscoresRegex();
}
