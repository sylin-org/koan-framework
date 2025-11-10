using Koan.Context.Models;
using Koan.Data.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Context.Bootstrap;

/// <summary>
/// Seeds default search categories and audiences on first run
/// </summary>
/// <remarks>
/// Creates 7 default categories: guide, adr, sample, test, documentation, source, reference
/// Creates 6 default audiences: learner, developer, architect, pm, executive, contributor
/// Runs only if SearchCategory table is empty (idempotent)
/// </remarks>
public class SearchProfileSeeder : IHostedService
{
    private readonly ILogger<SearchProfileSeeder> _logger;

    public SearchProfileSeeder(ILogger<SearchProfileSeeder> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            // Check if already seeded
            var existingCategories = await SearchCategory.All(ct);
            if (existingCategories.Any())
            {
                _logger.LogInformation("Search profiles already seeded ({Count} categories), skipping", existingCategories.Count());
                return;
            }

            _logger.LogInformation("Seeding search categories and audiences...");

            await SeedCategoriesAsync(ct);
            await SeedAudiencesAsync(ct);

            _logger.LogInformation("Search profiles seeded successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to seed search profiles");
            // Don't throw - allow app to continue even if seeding fails
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private async Task SeedCategoriesAsync(CancellationToken ct)
    {
        var categories = new[]
        {
            SearchCategory.Create(
                name: "guide",
                displayName: "Developer Guides",
                description: "Step-by-step developer guides and tutorials",
                pathPatterns: new() { "docs/guides/**", "guides/**" },
                priority: 10,
                defaultAlpha: 0.4f),

            SearchCategory.Create(
                name: "adr",
                displayName: "Architecture Decisions",
                description: "Architectural Decision Records explaining design choices",
                pathPatterns: new() { "docs/decisions/**", "adrs/**", "decisions/**" },
                priority: 9,
                defaultAlpha: 0.3f),

            SearchCategory.Create(
                name: "sample",
                displayName: "Code Samples",
                description: "Example implementations and sample applications",
                pathPatterns: new() { "samples/**", "examples/**" },
                priority: 8,
                defaultAlpha: 0.5f),

            SearchCategory.Create(
                name: "test",
                displayName: "Test Code",
                description: "Test code showing usage patterns",
                pathPatterns: new() { "**/tests/**", "**/*.test.cs", "**/*.spec.cs" },
                priority: 6,
                defaultAlpha: 0.6f),

            SearchCategory.Create(
                name: "documentation",
                displayName: "General Documentation",
                description: "General documentation, READMEs, and overviews",
                pathPatterns: new() { "docs/**", "**/readme.md", "**/README.md" },
                priority: 7,
                defaultAlpha: 0.4f),

            SearchCategory.Create(
                name: "source",
                displayName: "Source Code",
                description: "Implementation source code",
                pathPatterns: new() { "src/**" },
                priority: 4,
                defaultAlpha: 0.7f),

            SearchCategory.Create(
                name: "reference",
                displayName: "API Reference",
                description: "API documentation and technical references",
                pathPatterns: new() { "docs/api/**", "docs/reference/**" },
                priority: 8,
                defaultAlpha: 0.3f)
        };

        foreach (var category in categories)
        {
            await category.Save(ct);
            _logger.LogDebug("Seeded category: {Name} ({DisplayName})", category.Name, category.DisplayName);
        }

        _logger.LogInformation("Seeded {Count} search categories", categories.Length);
    }

    private async Task SeedAudiencesAsync(CancellationToken ct)
    {
        var audiences = new[]
        {
            SearchAudience.Create(
                name: "learner",
                displayName: "Developer Learning Koan",
                description: "New developers learning the framework",
                categoryNames: new() { "guide", "sample", "test" },
                defaultAlpha: 0.4f,
                maxTokens: 6000),

            SearchAudience.Create(
                name: "developer",
                displayName: "Active Developer",
                description: "Developers actively building with Koan",
                categoryNames: new() { "guide", "sample", "source", "test", "reference" },
                defaultAlpha: 0.5f,
                maxTokens: 8000),

            SearchAudience.Create(
                name: "architect",
                displayName: "Software Architect",
                description: "Technical leaders and architects",
                categoryNames: new() { "adr", "source", "documentation" },
                defaultAlpha: 0.3f,
                maxTokens: 5000),

            SearchAudience.Create(
                name: "pm",
                displayName: "Product Manager",
                description: "Product and project managers",
                categoryNames: new() { "adr", "guide", "documentation" },
                defaultAlpha: 0.3f,
                maxTokens: 4000),

            SearchAudience.Create(
                name: "executive",
                displayName: "Executive/Leadership",
                description: "Technical leadership and executives",
                categoryNames: new() { "adr", "documentation" },
                defaultAlpha: 0.2f,
                maxTokens: 3000),

            SearchAudience.Create(
                name: "contributor",
                displayName: "Framework Contributor",
                description: "Contributors to Koan Framework",
                categoryNames: new() { "source", "test", "adr" },
                defaultAlpha: 0.6f,
                maxTokens: 10000)
        };

        foreach (var audience in audiences)
        {
            await audience.Save(ct);
            _logger.LogDebug("Seeded audience: {Name} ({DisplayName})", audience.Name, audience.DisplayName);
        }

        _logger.LogInformation("Seeded {Count} search audiences", audiences.Length);
    }
}
