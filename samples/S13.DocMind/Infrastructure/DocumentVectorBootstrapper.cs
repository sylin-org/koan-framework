using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Vector;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using S13.DocMind.Models;
using S13.DocMind.Services;

namespace S13.DocMind.Infrastructure;

public sealed class DocumentVectorBootstrapper : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DocMindVectorHealth _vectorHealth;
    private readonly TimeProvider _clock;
    private readonly ILogger<DocumentVectorBootstrapper> _logger;

    public DocumentVectorBootstrapper(
        IServiceScopeFactory scopeFactory,
        DocMindVectorHealth vectorHealth,
        TimeProvider clock,
        ILogger<DocumentVectorBootstrapper> logger)
    {
        _scopeFactory = scopeFactory;
        _vectorHealth = vectorHealth;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Vector<DocumentChunkEmbedding>.IsAvailable)
        {
            _logger.LogInformation("Vector provider not configured; skipping DocMind vector bootstrap");
            _vectorHealth.RecordAudit(false, Array.Empty<string>(), "Vector adapter unavailable");
            return;
        }

        try
        {
            await Vector<DocumentChunkEmbedding>.EnsureCreated(stoppingToken).ConfigureAwait(false);
            await Vector<SemanticTypeEmbedding>.EnsureCreated(stoppingToken).ConfigureAwait(false);
            _logger.LogInformation("DocMind vector indexes ensured");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to ensure DocMind vector indexes");
            _vectorHealth.RecordAudit(true, Array.Empty<string>(), ex.Message);
        }

        await AuditSemanticProfilesAsync(stoppingToken).ConfigureAwait(false);
    }

    private async Task AuditSemanticProfilesAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var generator = scope.ServiceProvider.GetRequiredService<IEmbeddingGenerator>();

            var profiles = (await SemanticTypeProfile.All(stoppingToken).ConfigureAwait(false)).ToList();
            var embeddings = await SemanticTypeEmbedding.All(stoppingToken).ConfigureAwait(false);
            var embeddingMap = embeddings
                .Where(e => e.SemanticTypeProfileId != Guid.Empty)
                .ToDictionary(e => e.SemanticTypeProfileId, e => e);

            var missingProfiles = new List<string>();
            foreach (var profile in profiles.Where(p => !p.Archived))
            {
                if (!Guid.TryParse(profile.Id, out var profileId))
                {
                    continue;
                }

                if (embeddingMap.ContainsKey(profileId))
                {
                    continue;
                }

                var seedText = ResolveSampleText(profile);
                if (string.IsNullOrWhiteSpace(seedText))
                {
                    missingProfiles.Add(profile.Name);
                    continue;
                }

                var result = await generator.GenerateAsync(seedText, stoppingToken).ConfigureAwait(false);
                _vectorHealth.RecordGeneration(result.Duration, result.Model, result.HasEmbedding);
                if (!result.HasEmbedding)
                {
                    missingProfiles.Add(profile.Name);
                    continue;
                }

                var entity = new SemanticTypeEmbedding
                {
                    SemanticTypeProfileId = profileId,
                    Embedding = result.Embedding!,
                    GeneratedAt = _clock.GetUtcNow()
                };

                await entity.Save(stoppingToken).ConfigureAwait(false);
            }

            _vectorHealth.RecordAudit(true, missingProfiles, missingProfiles.Count == 0 ? null : "Missing profile vectors");
            if (missingProfiles.Count > 0)
            {
                _logger.LogWarning("Semantic profile embeddings missing for: {Profiles}", string.Join(", ", missingProfiles));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vector semantic profile audit failed");
            _vectorHealth.RecordAudit(true, Array.Empty<string>(), ex.Message);
        }
    }

    private static string ResolveSampleText(SemanticTypeProfile profile)
    {
        if (profile.Metadata.TryGetValue("sample", out var sample) && !string.IsNullOrWhiteSpace(sample))
        {
            return sample;
        }

        if (profile.ExamplePhrases.Count > 0)
        {
            return string.Join(Environment.NewLine, profile.ExamplePhrases);
        }

        return profile.Description ?? profile.Name;
    }
}
