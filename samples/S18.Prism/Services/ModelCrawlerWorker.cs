using System.Text.Json;
using System.Text.Json.Serialization;
using Koan.Data.Core;
using S18.Prism.Models;

namespace S18.Prism.Services;

/// <summary>
/// Background worker that crawls HuggingFace for model cards and stores them
/// as ModelCard entities for semantic search in the knowledge base.
/// </summary>
public sealed class ModelCrawlerWorker : BackgroundService
{
    private readonly ILogger<ModelCrawlerWorker> _logger;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _configuration;

    private const string HfModelsUrl = "https://huggingface.co/api/models";
    private const int ModelsPerCategory = 50;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ModelCrawlerWorker(
        ILogger<ModelCrawlerWorker> logger,
        IHttpClientFactory httpFactory,
        IConfiguration configuration)
    {
        _logger = logger;
        _httpFactory = httpFactory;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _configuration.GetValue("Prism:ModelCrawler:Enabled", true);
        if (!enabled)
        {
            _logger.LogInformation("ModelCrawlerWorker is disabled via configuration");
            return;
        }

        _logger.LogInformation("ModelCrawlerWorker started");

        // Allow framework boot to complete
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CrawlAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in ModelCrawlerWorker cycle");
            }

            var interval = ResolveScheduleInterval();
            _logger.LogInformation("ModelCrawlerWorker sleeping for {Interval}", interval);
            await Task.Delay(interval, stoppingToken);
        }

        _logger.LogInformation("ModelCrawlerWorker stopped");
    }

    private async Task CrawlAsync(CancellationToken ct)
    {
        var categories = _configuration
            .GetSection("Prism:ModelCrawler:SeedCategories")
            .Get<List<string>>() ?? DefaultCategories;

        _logger.LogInformation("Crawling HuggingFace for {Count} categories: {Categories}",
            categories.Count, string.Join(", ", categories));

        var totalNew = 0;
        var totalUpdated = 0;

        foreach (var category in categories)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var (newCount, updatedCount) = await CrawlCategoryAsync(category, ct);
                totalNew += newCount;
                totalUpdated += updatedCount;

                // Rate limit between category fetches
                await Task.Delay(500, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to crawl category {Category}", category);
            }
        }

        _logger.LogInformation(
            "ModelCrawler completed: {NewCount} new, {UpdatedCount} updated models",
            totalNew, totalUpdated);
    }

    private async Task<(int NewCount, int UpdatedCount)> CrawlCategoryAsync(
        string category, CancellationToken ct)
    {
        var url = $"{HfModelsUrl}?pipeline_tag={category}&limit={ModelsPerCategory}&sort=downloads&direction=-1";

        _logger.LogDebug("Fetching models for category: {Category}", category);

        using var http = _httpFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "Koan-Prism/1.0");

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var models = JsonSerializer.Deserialize<List<HfModel>>(json, JsonOptions) ?? [];

        var newCount = 0;
        var updatedCount = 0;

        foreach (var model in models)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(model.Id))
                continue;

            try
            {
                var existing = await ModelCard.Query(m => m.HubId == model.Id, ct);

                if (existing.Count > 0)
                {
                    var card = existing[0];

                    // Update if downloads increased >10%
                    if (card.Downloads > 0 &&
                        model.Downloads > card.Downloads * 1.1)
                    {
                        var updatedCard = UpdateCard(card, model, category);
                        await updatedCard.Save(ct);
                        updatedCount++;

                        _logger.LogDebug("Updated model card {HubId}: downloads {Old} -> {New}",
                            model.Id, card.Downloads, model.Downloads);
                    }
                }
                else
                {
                    var card = CreateCard(model, category);
                    await card.Save(ct);
                    newCount++;

                    _logger.LogDebug("Created model card {HubId}", model.Id);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to process model {ModelId}", model.Id);
            }
        }

        _logger.LogInformation(
            "Category {Category}: {New} new, {Updated} updated out of {Total} models",
            category, newCount, updatedCount, models.Count);

        return (newCount, updatedCount);
    }

    private static ModelCard CreateCard(HfModel model, string category) =>
        new()
        {
            HubId = model.Id ?? "",
            Provider = "huggingface",
            Author = model.Author ?? ExtractAuthor(model.Id),
            Title = model.Id ?? "",
            Description = BuildDescription(model, category),
            Tags = model.Tags ?? [],
            Task = model.PipelineTag ?? category,
            Downloads = model.Downloads,
            Likes = model.Likes,
            LastModified = model.LastModified ?? DateTime.UtcNow,
            CrawledAt = DateTime.UtcNow
        };

    private static ModelCard UpdateCard(ModelCard card, HfModel model, string category)
    {
        card.Downloads = model.Downloads;
        card.Likes = model.Likes;
        card.Tags = model.Tags ?? card.Tags;
        card.LastModified = model.LastModified ?? card.LastModified;
        card.Description = BuildDescription(model, category);
        card.CrawledAt = DateTime.UtcNow;
        return card;
    }

    private static string BuildDescription(HfModel model, string category)
    {
        var parts = new List<string>();
        parts.Add($"Task: {model.PipelineTag ?? category}");

        if (model.Tags is { Count: > 0 })
            parts.Add($"Tags: {string.Join(", ", model.Tags.Take(10))}");

        parts.Add($"Downloads: {model.Downloads:N0}");
        parts.Add($"Likes: {model.Likes:N0}");

        return string.Join(" | ", parts);
    }

    private static string? ExtractAuthor(string? hubId)
    {
        if (hubId is null) return null;
        var slashIdx = hubId.IndexOf('/');
        return slashIdx > 0 ? hubId[..slashIdx] : null;
    }

    private TimeSpan ResolveScheduleInterval()
    {
        var schedule = _configuration.GetValue("Prism:ModelCrawler:Schedule", "Daily");
        return schedule?.ToLowerInvariant() switch
        {
            "hourly" => TimeSpan.FromHours(1),
            "daily" => TimeSpan.FromDays(1),
            "weekly" => TimeSpan.FromDays(7),
            _ => TimeSpan.FromDays(1)
        };
    }

    private static readonly List<string> DefaultCategories =
    [
        "text-generation",
        "text2text-generation",
        "feature-extraction",
        "image-classification",
        "automatic-speech-recognition"
    ];

    private sealed record HfModel
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("modelId")]
        public string? ModelId { get; init; }

        [JsonPropertyName("author")]
        public string? Author { get; init; }

        [JsonPropertyName("downloads")]
        public int Downloads { get; init; }

        [JsonPropertyName("likes")]
        public int Likes { get; init; }

        [JsonPropertyName("tags")]
        public List<string>? Tags { get; init; }

        [JsonPropertyName("pipeline_tag")]
        public string? PipelineTag { get; init; }

        [JsonPropertyName("lastModified")]
        public DateTime? LastModified { get; init; }
    }
}
