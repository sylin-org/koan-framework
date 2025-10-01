using System.Text.RegularExpressions;

namespace Koan.Data.Core;

/// <summary>
/// Validates partition names according to framework naming rules.
///
/// Rules:
/// - MUST start with a letter (a-z, A-Z)
/// - MAY contain alphanumeric characters, hyphen (-), or period (.)
/// - MUST NOT end with hyphen or period
/// - Case-sensitive (if adapter supports it)
/// - Single letter is valid (e.g., "a", "B")
///
/// Valid examples: "archive", "cold-tier", "backup.v2", "A", "prod-us-east-1"
/// Invalid examples: "1archive" (starts with digit), "backup-" (ends with hyphen), "test." (ends with period)
/// </summary>
internal static class PartitionNameValidator
{
    // Pattern explanation:
    // ^[a-zA-Z]                    - Must start with letter
    // [a-zA-Z0-9\-\.]*            - Zero or more alphanumeric, hyphen, or period
    // [a-zA-Z0-9]$                - Must end with alphanumeric
    // |^[a-zA-Z]$                 - OR single letter (special case)
    private static readonly Regex PartitionNamePattern = new(
        @"^[a-zA-Z][a-zA-Z0-9\-\.]*[a-zA-Z0-9]$|^[a-zA-Z]$",
        RegexOptions.Compiled);

    /// <summary>
    /// Validate partition name against framework rules.
    /// </summary>
    /// <param name="name">Partition name to validate</param>
    /// <returns>True if valid, false otherwise</returns>
    public static bool IsValid(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return PartitionNamePattern.IsMatch(name);
    }
}
