using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Data.Core;
using Koan.Samples.Meridian.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Samples.Meridian.Services;

public sealed class ClassificationSeedService : IHostedService
{
    private readonly MeridianOptions _options;
    private readonly ILogger<ClassificationSeedService> _logger;

    public ClassificationSeedService(IOptions<MeridianOptions> options, ILogger<ClassificationSeedService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var typeOption in _options.Classification.Types)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var existing = await SourceType.Get(typeOption.Id, cancellationToken);
            if (existing is null)
            {
                var created = new SourceType
                {
                    Id = typeOption.Id,
                    Name = typeOption.Name,
                    Description = typeOption.Description,
                    Version = typeOption.Version,
                    Tags = new List<string>(typeOption.Tags),
                    Descriptors = new List<string>(typeOption.Descriptors),
                    FilenamePatterns = new List<string>(typeOption.FilenamePatterns),
                    Keywords = new List<string>(typeOption.Keywords),
                    ExpectedPageCountMin = typeOption.ExpectedPageCountMin,
                    ExpectedPageCountMax = typeOption.ExpectedPageCountMax,
                    MimeTypes = new List<string>(typeOption.MimeTypes),
                    FieldQueries = new Dictionary<string, string>(typeOption.FieldQueries, StringComparer.OrdinalIgnoreCase),
                    Instructions = typeOption.Instructions ?? string.Empty,
                    OutputTemplate = typeOption.OutputTemplate ?? string.Empty,
                    InstructionsUpdatedAt = DateTime.UtcNow,
                    OutputTemplateUpdatedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await created.Save(cancellationToken);
                _logger.LogInformation("Seeded classification type {TypeId} (v{Version}).", created.Id, created.Version);
                continue;
            }

            var changed = existing.Version != typeOption.Version ||
                          !SequenceEqual(existing.FilenamePatterns, typeOption.FilenamePatterns) ||
                          !SequenceEqual(existing.Keywords, typeOption.Keywords) ||
                          existing.ExpectedPageCountMin != typeOption.ExpectedPageCountMin ||
                          existing.ExpectedPageCountMax != typeOption.ExpectedPageCountMax ||
                          !SequenceEqual(existing.MimeTypes, typeOption.MimeTypes) ||
                          !SequenceEqual(existing.Tags, typeOption.Tags) ||
                          !SequenceEqual(existing.Descriptors, typeOption.Descriptors) ||
                          !DictionaryEqual(existing.FieldQueries, typeOption.FieldQueries) ||
                          !string.Equals(existing.Instructions, typeOption.Instructions, StringComparison.Ordinal) ||
                          !string.Equals(existing.OutputTemplate, typeOption.OutputTemplate, StringComparison.Ordinal) ||
                          !string.Equals(existing.Name, typeOption.Name, StringComparison.Ordinal) ||
                          !string.Equals(existing.Description, typeOption.Description, StringComparison.Ordinal);

            if (!changed)
            {
                continue;
            }

            existing.Name = typeOption.Name;
            existing.Description = typeOption.Description;
            existing.Version = typeOption.Version;
            existing.Tags = new List<string>(typeOption.Tags);
            existing.Descriptors = new List<string>(typeOption.Descriptors);
            existing.FilenamePatterns = new List<string>(typeOption.FilenamePatterns);
            existing.Keywords = new List<string>(typeOption.Keywords);
            existing.ExpectedPageCountMin = typeOption.ExpectedPageCountMin;
            existing.ExpectedPageCountMax = typeOption.ExpectedPageCountMax;
            existing.MimeTypes = new List<string>(typeOption.MimeTypes);
            existing.FieldQueries = new Dictionary<string, string>(typeOption.FieldQueries, StringComparer.OrdinalIgnoreCase);
            if (!string.Equals(existing.Instructions, typeOption.Instructions, StringComparison.Ordinal))
            {
                existing.Instructions = typeOption.Instructions ?? string.Empty;
                existing.InstructionsUpdatedAt = DateTime.UtcNow;
            }
            if (!string.Equals(existing.OutputTemplate, typeOption.OutputTemplate, StringComparison.Ordinal))
            {
                existing.OutputTemplate = typeOption.OutputTemplate ?? string.Empty;
                existing.OutputTemplateUpdatedAt = DateTime.UtcNow;
            }
            existing.TypeEmbedding = null;
            existing.TypeEmbeddingVersion = 0;
            existing.TypeEmbeddingHash = null;
            existing.TypeEmbeddingComputedAt = null;
            existing.UpdatedAt = DateTime.UtcNow;

            await existing.Save(cancellationToken);
            _logger.LogInformation("Updated classification type {TypeId} to version {Version}.", existing.Id, existing.Version);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static bool SequenceEqual(IReadOnlyCollection<string> left, IReadOnlyCollection<string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        return left.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(right.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
    }

    private static bool DictionaryEqual(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var kvp in left)
        {
            if (!right.TryGetValue(kvp.Key, out var otherValue))
            {
                return false;
            }

            if (!string.Equals(kvp.Value, otherValue, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
