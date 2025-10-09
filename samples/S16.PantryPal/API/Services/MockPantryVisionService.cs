using System.Diagnostics;
using S16.PantryPal.Models;

namespace S16.PantryPal.Services;

/// <summary>
/// Mock vision service for demonstration and testing.
/// Returns realistic detection results without requiring actual AI infrastructure.
///
/// In production, replace with OllamaVisionService or OpenAIVisionService.
/// </summary>
public class MockPantryVisionService : IPantryVisionService
{
    private readonly Dictionary<string, string> _userCorrections = new();

    // Sample detection patterns for demo
    private static readonly List<(string Name, string[] Alternatives, string Category, string DefaultUnit)> SampleItems = new()
    {
        ("chicken breast", new[] { "chicken", "poultry", "chicken thigh" }, "meat", "lbs"),
        ("black beans", new[] { "beans", "canned beans", "kidney beans" }, "canned", "whole"),
        ("milk", new[] { "dairy", "whole milk", "2% milk" }, "dairy", "gallon"),
        ("eggs", new[] { "chicken eggs", "large eggs" }, "dairy", "dozen"),
        ("tomatoes", new[] { "tomato", "roma tomatoes", "cherry tomatoes" }, "produce", "whole"),
        ("olive oil", new[] { "oil", "extra virgin olive oil", "cooking oil" }, "pantry", "bottle"),
        ("rice", new[] { "white rice", "brown rice", "long grain rice" }, "pantry", "lbs"),
        ("pasta", new[] { "spaghetti", "penne", "noodles" }, "pantry", "package"),
        ("cheddar cheese", new[] { "cheese", "sharp cheddar", "mild cheddar" }, "dairy", "lbs"),
        ("spinach", new[] { "leafy greens", "baby spinach", "fresh spinach" }, "produce", "bunch")
    };

    public async Task<VisionProcessingResult> ProcessPhotoAsync(
        string photoId,
        Stream imageStream,
        VisionProcessingOptions options,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        // Simulate processing delay
        await Task.Delay(800, cancellationToken);

        // Generate mock detections (simulate 2-3 items detected)
        var random = new Random(photoId.GetHashCode());
        var itemCount = random.Next(2, 4);
        var detections = new List<PantryDetection>();

        var imageWidth = 800;
        var imageHeight = 600;

        for (int i = 0; i < itemCount; i++)
        {
            var item = SampleItems[random.Next(SampleItems.Count)];

            // Generate realistic bounding box
            var x = random.Next(50, imageWidth - 250);
            var y = random.Next(50, imageHeight - 200);
            var width = random.Next(150, 250);
            var height = random.Next(120, 200);

            // Generate confidence scores
            var topConfidence = (float)(0.85 + random.NextDouble() * 0.14); // 0.85-0.99
            var altConfidence1 = topConfidence - (float)(random.NextDouble() * 0.15);
            var altConfidence2 = altConfidence1 - (float)(random.NextDouble() * 0.15);

            // Create candidates
            var candidates = new List<DetectionCandidate>
            {
                new()
                {
                    Name = item.Name,
                    Confidence = topConfidence,
                    Category = item.Category,
                    DefaultUnit = item.DefaultUnit,
                    TypicalShelfLifeDays = GetTypicalShelfLife(item.Category)
                }
            };

            // Add alternatives if available
            if (item.Alternatives.Length > 0)
            {
                candidates.Add(new DetectionCandidate
                {
                    Name = item.Alternatives[0],
                    Confidence = altConfidence1,
                    Category = item.Category,
                    DefaultUnit = item.DefaultUnit
                });

                if (item.Alternatives.Length > 1)
                {
                    candidates.Add(new DetectionCandidate
                    {
                        Name = item.Alternatives[1],
                        Confidence = altConfidence2,
                        Category = item.Category,
                        DefaultUnit = item.DefaultUnit
                    });
                }
            }

            // Simulate quantity detection (70% chance)
            decimal? detectedQuantity = null;
            string? detectedUnit = null;
            if (random.NextDouble() > 0.3)
            {
                detectedQuantity = random.Next(1, 4);
                detectedUnit = item.DefaultUnit;
            }

            // Simulate expiration date detection (50% chance)
            DateTime? detectedExpiration = null;
            if (random.NextDouble() > 0.5)
            {
                var daysAhead = random.Next(3, 30);
                detectedExpiration = DateTime.UtcNow.AddDays(daysAhead);
            }

            // Simulate barcode detection (30% chance for packaged items)
            string? barcode = null;
            if (item.Category != "produce" && random.NextDouble() > 0.7)
            {
                barcode = GenerateMockBarcode();
            }

            var detection = new PantryDetection
            {
                BoundingBox = new BoundingBox
                {
                    X = x,
                    Y = y,
                    Width = width,
                    Height = height
                },
                Candidates = candidates.ToArray(),
                SelectedCandidateId = candidates[0].Id,
                Status = "pending"
            };

            // Set parsed data if quantities/expiration detected
            if (detectedQuantity.HasValue || detectedExpiration.HasValue)
            {
                detection.ParsedData = new ParsedItemData
                {
                    Quantity = detectedQuantity,
                    Unit = detectedUnit,
                    ExpiresAt = detectedExpiration,
                    Confidence = detectedExpiration.HasValue ? ExpirationParseConfidence.High : ExpirationParseConfidence.Unparsed,
                    WasParsed = true
                };
            }

            detections.Add(detection);
        }

        sw.Stop();

        // Calculate metrics
        var metrics = new VisionMetrics
        {
            ItemsDetected = detections.Count,
            HighConfidenceItems = detections.Count(d => d.Candidates[0].Confidence >= 0.9f),
            MediumConfidenceItems = detections.Count(d => d.Candidates[0].Confidence >= 0.7f && d.Candidates[0].Confidence < 0.9f),
            LowConfidenceItems = detections.Count(d => d.Candidates[0].Confidence < 0.7f),
            ExpirationDatesDetected = detections.Count(d => d.ParsedData?.ExpiresAt.HasValue == true),
            BarcodesDetected = 0 // Mock doesn't generate barcodes
        };

        return new VisionProcessingResult
        {
            Success = true,
            Detections = detections.ToArray(),
            ProcessingTimeMs = (int)sw.ElapsedMilliseconds,
            Metrics = metrics
        };
    }

    public Task LearnFromCorrectionAsync(
        string originalName,
        string correctedName,
        string? correctedQuantity,
        CancellationToken cancellationToken = default)
    {
        // Track correction for future reference
        _userCorrections[originalName.ToLowerInvariant()] = correctedName;

        // In a real implementation, this would:
        // 1. Store correction in VisionSettings.UserCorrections
        // 2. Potentially trigger model fine-tuning
        // 3. Update confidence scoring for similar items

        return Task.CompletedTask;
    }

    private static int GetTypicalShelfLife(string category)
    {
        return category switch
        {
            "produce" => 7,
            "dairy" => 10,
            "meat" => 5,
            "canned" => 730, // 2 years
            "pantry" => 365, // 1 year
            _ => 30
        };
    }

    private static string GenerateMockBarcode()
    {
        var random = new Random();
        return $"0{random.Next(10000000, 99999999)}{random.Next(1000, 9999)}";
    }
}

/// <summary>
/// Extension methods for service registration.
/// </summary>
public static class VisionServiceExtensions
{
    public static IServiceCollection AddPantryVision(this IServiceCollection services)
    {
        services.AddSingleton<IPantryVisionService, MockPantryVisionService>();
        services.AddSingleton<IPantryInputParser, PantryInputParser>();

        return services;
    }
}
