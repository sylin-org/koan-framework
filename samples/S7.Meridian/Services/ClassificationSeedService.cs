using Koan.Samples.Meridian.Infrastructure;
using Koan.Data.Core;
using System.Linq;
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
                    FilenamePatterns = new List<string>(typeOption.FilenamePatterns),
                    Keywords = new List<string>(typeOption.Keywords),
                    ExpectedPageCountMin = typeOption.ExpectedPageCountMin,
                    ExpectedPageCountMax = typeOption.ExpectedPageCountMax,
                    MimeTypes = new List<string>(typeOption.MimeTypes),
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
                          !string.Equals(existing.Name, typeOption.Name, StringComparison.Ordinal) ||
                          !string.Equals(existing.Description, typeOption.Description, StringComparison.Ordinal);

            if (!changed)
            {
                continue;
            }

            existing.Name = typeOption.Name;
            existing.Description = typeOption.Description;
            existing.Version = typeOption.Version;
            existing.FilenamePatterns = new List<string>(typeOption.FilenamePatterns);
            existing.Keywords = new List<string>(typeOption.Keywords);
            existing.ExpectedPageCountMin = typeOption.ExpectedPageCountMin;
            existing.ExpectedPageCountMax = typeOption.ExpectedPageCountMax;
            existing.MimeTypes = new List<string>(typeOption.MimeTypes);
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
}
