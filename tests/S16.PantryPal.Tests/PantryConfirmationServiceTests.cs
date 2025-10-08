using FluentAssertions;
using S16.PantryPal.Services;
using S16.PantryPal.Models;
using S16.PantryPal.Contracts;

namespace S16.PantryPal.Tests;

public class PantryConfirmationServiceTests
{
    private sealed class FakeVisionService : IPantryVisionService
    {
        public Task<VisionProcessingResult> ProcessPhotoAsync(string photoId, Stream image, VisionProcessingOptions options, CancellationToken ct = default) =>
            Task.FromResult(new VisionProcessingResult { Success = true, Detections = Array.Empty<VisionDetection>(), ProcessingTimeMs = 10 });

        public Task LearnFromCorrectionAsync(string original, string corrected, string userInput, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeParser : IPantryInputParser
    {
        public ParsedItemData? ParseInput(string input) => new ParsedItemData { Quantity = 2, Unit = "lbs", ExpiresAt = DateTime.UtcNow.AddDays(5) };
    }

    [Fact]
    public async Task ConfirmDetections_ShouldPersistItems_AndMarkPhoto()
    {
        var photo = new PantryPhoto
        {
            Detections = new []
            {
                new VisionDetection
                {
                    Id = "d1",
                    ParsedData = new ParsedItemData{ Quantity = 1, Unit = "unit" },
                    Candidates = new [] { new VisionCandidate { Id = "c1", Name = "apple", Confidence = 0.9f, DefaultUnit = "whole" } }
                }
            }
        };
        await photo.Save();

        var svc = new PantryConfirmationService();
        var result = await svc.ConfirmDetectionsAsync(photo.Id, new [] { new DetectionConfirmation{ DetectionId = "d1", UserInput = "2 lbs" } }, new FakeVisionService(), new FakeParser());

        result.Should().HaveCount(1);
        var item = result.Single();
        item.Name.Should().Be("apple");
        item.Quantity.Should().Be(2); // from parser
        (await PantryItem.Get(item.Id)).Should().NotBeNull();
        var reloadedPhoto = await PantryPhoto.Get(photo.Id);
        reloadedPhoto!.ItemsConfirmed.Should().Be(1);
    }
}
