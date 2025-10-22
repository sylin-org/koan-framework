using System;
using System.Collections.Generic;
using Koan.Data.Core;
using Koan.Samples.Meridian.Models;
using Microsoft.Extensions.Logging;

namespace Koan.Samples.Meridian.Services;

public interface IAiAssistAuditor
{
    Task RecordAsync(
        string entityType,
        string? suggestedName,
        string requestSummary,
        string responseSummary,
        string? model,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken ct);
}

public sealed class AiAssistAuditor : IAiAssistAuditor
{
    private readonly ILogger<AiAssistAuditor> _logger;

    public AiAssistAuditor(ILogger<AiAssistAuditor> logger)
    {
        _logger = logger;
    }

    public async Task RecordAsync(
        string entityType,
        string? suggestedName,
        string requestSummary,
        string responseSummary,
        string? model,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entityType))
        {
            throw new ArgumentException("Entity type is required.", nameof(entityType));
        }

        var evt = new AiAssistEvent
        {
            EntityType = entityType.Trim(),
            SuggestedEntityName = suggestedName?.Trim(),
            RequestSummary = SafeTrim(requestSummary, 1024),
            ResponseSummary = SafeTrim(responseSummary, 2048),
            Model = model?.Trim(),
            Metadata = metadata is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase),
            CreatedAt = DateTime.UtcNow
        };

        await evt.Save(ct).ConfigureAwait(false);
        _logger.LogInformation("Recorded AI assist event for {EntityType} (name: {Name}).", evt.EntityType, evt.SuggestedEntityName);
    }

    private static string SafeTrim(string value, int maxLength)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        return trimmed[..maxLength];
    }
}
