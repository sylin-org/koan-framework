using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Samples.Meridian.Models;
using Microsoft.Extensions.Logging;

namespace Koan.Samples.Meridian.Services;

public interface IFieldConflictResolver
{
    List<ExtractedField> Resolve(List<ExtractedField> allFields);
}

/// <summary>
/// Stage 4: Resolve conflicts when multiple sources provide values for the same field.
/// Uses precedence rules: AuthoritativeNotes (1) > UserReview (2) > DocumentExtraction (3).
/// </summary>
public sealed class FieldConflictResolver : IFieldConflictResolver
{
    private readonly ILogger<FieldConflictResolver> _logger;

    public FieldConflictResolver(ILogger<FieldConflictResolver> logger)
    {
        _logger = logger;
    }

    public List<ExtractedField> Resolve(List<ExtractedField> allFields)
    {
        if (allFields.Count == 0)
        {
            return new List<ExtractedField>();
        }

        // Group fields by FieldPath
        var groupedByPath = allFields
            .GroupBy(f => f.FieldPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var resolved = new List<ExtractedField>();

        foreach (var group in groupedByPath)
        {
            var fieldPath = group.Key;
            var candidates = group.ToList();

            if (candidates.Count == 1)
            {
                // No conflict
                resolved.Add(candidates[0]);
                continue;
            }

            // Conflict: multiple sources for same field
            // Sort by precedence (lower number = higher priority), then by confidence
            var winner = candidates
                .OrderBy(f => f.Precedence)
                .ThenByDescending(f => f.Confidence)
                .ThenByDescending(f => f.CreatedAt)
                .First();

            resolved.Add(winner);

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                var sourcesSummary = string.Join(", ", candidates
                    .GroupBy(f => f.Source)
                    .Select(g => $"{g.Key}({g.Count()})"));

                _logger.LogDebug(
                    "Resolved conflict for {FieldPath}: {WinnerSource} (precedence={Precedence}, confidence={Confidence:F2}) won over {OtherSources}",
                    fieldPath, winner.Source, winner.Precedence, winner.Confidence, sourcesSummary);
            }
        }

        var totalConflicts = groupedByPath.Count(g => g.Count() > 1);
        if (totalConflicts > 0)
        {
            _logger.LogInformation(
                "Resolved {ConflictCount} field conflicts: {NotesFacts} from notes, {DocFacts} from documents, {ManualFacts} from manual override",
                totalConflicts,
                resolved.Count(f => f.Source == FieldSource.AuthoritativeNotes),
                resolved.Count(f => f.Source == FieldSource.DocumentExtraction),
                resolved.Count(f => f.Source == FieldSource.ManualOverride));
        }

        return resolved;
    }
}
