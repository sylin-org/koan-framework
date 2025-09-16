using Microsoft.Extensions.Logging;
using S5.Recs.Models;
using Koan.Data.Core;

namespace S5.Recs.Services;

/// <summary>
/// Bootstraps essential reference data for the multi-media system.
/// </summary>
public class DataBootstrapper
{
    private readonly ILogger<DataBootstrapper>? _logger;

    public DataBootstrapper(ILogger<DataBootstrapper>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Seeds MediaType and MediaFormat reference data if not already present.
    /// </summary>
    public async Task SeedReferenceDataAsync(CancellationToken ct = default)
    {
        Console.Error.WriteLine("[DEBUG] DataBootstrapper.SeedReferenceDataAsync() called");
        _logger?.LogInformation("[DEBUG] DataBootstrapper.SeedReferenceDataAsync() called");
        await SeedMediaTypesAsync(ct);
        await SeedMediaFormatsAsync(ct);
        Console.Error.WriteLine("[DEBUG] DataBootstrapper.SeedReferenceDataAsync() completed");
        _logger?.LogInformation("[DEBUG] DataBootstrapper.SeedReferenceDataAsync() completed");
    }

    private async Task SeedMediaTypesAsync(CancellationToken ct)
    {
        var threadId = Thread.CurrentThread.ManagedThreadId;
        Console.Error.WriteLine($"[DEBUG] SeedMediaTypesAsync() called on thread {threadId} at {DateTime.UtcNow:HH:mm:ss.fff}");
        _logger?.LogInformation("[DEBUG] SeedMediaTypesAsync() called");
        var existing = await MediaType.All(ct);
        Console.Error.WriteLine($"[DEBUG] Thread {threadId}: Found {existing.Count} existing MediaTypes: {string.Join(", ", existing.Select(mt => $"'{mt.Name}'"))}");
        _logger?.LogInformation("[DEBUG] Found {Count} existing MediaTypes: {Names}",
            existing.Count, string.Join(", ", existing.Select(mt => $"'{mt.Name}'")));
        if (existing.Any())
        {
            Console.Error.WriteLine($"[DEBUG] Thread {threadId}: MediaTypes already exist, returning early");
            _logger?.LogInformation("MediaTypes already seeded ({Count} found)", existing.Count);
            return;
        }

        var seedId = Guid.NewGuid().ToString("N")[..8];
        Console.Error.WriteLine($"[DEBUG] Thread {threadId}: No existing MediaTypes found, proceeding with seeding (Seed ID: {seedId})...");

        var mediaTypes = new[]
        {
            new MediaType
            {
                Id = "media-anime", // Stable ID for easier reference
                Name = "Anime",
                DisplayName = "Anime",
                Description = "Japanese animated series and films",
                SortOrder = 1
            },
            new MediaType
            {
                Id = "media-manga",
                Name = "Manga",
                DisplayName = "Manga",
                Description = "Japanese comics and graphic novels",
                SortOrder = 2
            },
            new MediaType
            {
                Id = "media-movie",
                Name = "Movie",
                DisplayName = "Movie",
                Description = "Live-action and animated films",
                SortOrder = 3
            },
            new MediaType
            {
                Id = "media-book",
                Name = "Book",
                DisplayName = "Book",
                Description = "Light novels and written content",
                SortOrder = 4
            }
        };

        await mediaTypes.Save(ct);

        Console.Error.WriteLine($"[DEBUG] Thread {threadId}: Successfully seeded {mediaTypes.Length} MediaTypes (Seed ID: {seedId})");
        _logger?.LogInformation("Seeded {Count} MediaTypes", mediaTypes.Length);
    }

    private async Task SeedMediaFormatsAsync(CancellationToken ct)
    {
        var existing = await MediaFormat.All(ct);
        if (existing.Any())
        {
            _logger?.LogInformation("MediaFormats already seeded ({Count} found)", existing.Count);
            return;
        }

        var mediaTypes = await MediaType.All(ct);
        var animeType = mediaTypes.FirstOrDefault(mt => mt.Name == "Anime");
        var mangaType = mediaTypes.FirstOrDefault(mt => mt.Name == "Manga");
        var movieType = mediaTypes.FirstOrDefault(mt => mt.Name == "Movie");
        var bookType = mediaTypes.FirstOrDefault(mt => mt.Name == "Book");

        var mediaFormats = new List<MediaFormat>();

        // Anime formats
        if (animeType != null)
        {
            mediaFormats.AddRange(new[]
            {
                new MediaFormat
                {
                    Id = Guid.NewGuid().ToString(),
                    MediaTypeId = animeType.Id!,
                    Name = "TV",
                    DisplayName = "TV Series",
                    Description = "Television anime series",
                    SortOrder = 1
                },
                new MediaFormat
                {
                    Id = Guid.NewGuid().ToString(),
                    MediaTypeId = animeType.Id!,
                    Name = "Movie",
                    DisplayName = "Movie",
                    Description = "Anime films",
                    SortOrder = 2
                },
                new MediaFormat
                {
                    Id = Guid.NewGuid().ToString(),
                    MediaTypeId = animeType.Id!,
                    Name = "OVA",
                    DisplayName = "OVA",
                    Description = "Original Video Animation",
                    SortOrder = 3
                },
                new MediaFormat
                {
                    Id = Guid.NewGuid().ToString(),
                    MediaTypeId = animeType.Id!,
                    Name = "Special",
                    DisplayName = "Special",
                    Description = "Special episodes",
                    SortOrder = 4
                }
            });
        }

        // Manga formats
        if (mangaType != null)
        {
            mediaFormats.AddRange(new[]
            {
                new MediaFormat
                {
                    Id = Guid.NewGuid().ToString(),
                    MediaTypeId = mangaType.Id!,
                    Name = "Manga",
                    DisplayName = "Manga",
                    Description = "Traditional manga series",
                    SortOrder = 1
                },
                new MediaFormat
                {
                    Id = Guid.NewGuid().ToString(),
                    MediaTypeId = mangaType.Id!,
                    Name = "Oneshot",
                    DisplayName = "One-shot",
                    Description = "Single chapter manga",
                    SortOrder = 2
                },
                new MediaFormat
                {
                    Id = Guid.NewGuid().ToString(),
                    MediaTypeId = mangaType.Id!,
                    Name = "Doujinshi",
                    DisplayName = "Doujinshi",
                    Description = "Self-published manga",
                    SortOrder = 3
                }
            });
        }

        // Movie formats
        if (movieType != null)
        {
            mediaFormats.AddRange(new[]
            {
                new MediaFormat
                {
                    Id = Guid.NewGuid().ToString(),
                    MediaTypeId = movieType.Id!,
                    Name = "Feature",
                    DisplayName = "Feature Film",
                    Description = "Full-length feature films",
                    SortOrder = 1
                },
                new MediaFormat
                {
                    Id = Guid.NewGuid().ToString(),
                    MediaTypeId = movieType.Id!,
                    Name = "Short",
                    DisplayName = "Short Film",
                    Description = "Short films",
                    SortOrder = 2
                }
            });
        }

        // Book formats
        if (bookType != null)
        {
            mediaFormats.AddRange(new[]
            {
                new MediaFormat
                {
                    Id = Guid.NewGuid().ToString(),
                    MediaTypeId = bookType.Id!,
                    Name = "LightNovel",
                    DisplayName = "Light Novel",
                    Description = "Japanese light novels",
                    SortOrder = 1
                },
                new MediaFormat
                {
                    Id = Guid.NewGuid().ToString(),
                    MediaTypeId = bookType.Id!,
                    Name = "Novel",
                    DisplayName = "Novel",
                    Description = "Full novels",
                    SortOrder = 2
                }
            });
        }

        if (mediaFormats.Any())
        {
            await MediaFormat.UpsertMany(mediaFormats, ct);
            _logger?.LogInformation("Seeded {Count} MediaFormats", mediaFormats.Count);
        }
    }
}