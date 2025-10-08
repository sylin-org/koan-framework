using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using S16.PantryPal.Controllers;
using S16.PantryPal.Services;
using S16.PantryPal.Models;
using S16.PantryPal.Contracts;

namespace S16.PantryPal.Tests;

public class IngestionFlowTests
{
    private sealed class FakeVision : IPantryVisionService
    {
        public Task<VisionProcessingResult> ProcessPhotoAsync(string photoId, Stream image, VisionProcessingOptions options, CancellationToken ct = default)
        {
            return Task.FromResult(new VisionProcessingResult
            {
                Success = true,
                ProcessingTimeMs = 12,
                Detections = new []
                {
                    new VisionDetection
                    {
                        Id = "d1",
                        Candidates = new [] { new VisionCandidate { Id = "c1", Name = "banana", Confidence = 0.88f, DefaultUnit = "whole" } },
                        ParsedData = new ParsedItemData { Quantity = 1, Unit = "whole" }
                    }
                }
            });
        }

        public Task LearnFromCorrectionAsync(string original, string corrected, string userInput, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeParser : IPantryInputParser
    {
        public ParsedItemData? ParseInput(string input) => new ParsedItemData { Quantity = 3, Unit = "whole", ExpiresAt = DateTime.UtcNow.AddDays(4) };
    }

    [Fact]
    public async Task Upload_And_Confirm_Should_Create_PantryItem()
    {
        var vision = new FakeVision();
        var parser = new FakeParser();
        var confirm = new PantryConfirmationService();

        var controller = new PantryIngestionController(vision, parser, confirm)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        // Simulate photo upload
        var bytes = new byte[] {1,2,3,4};
        var formFile = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "photo", "test.jpg")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/jpeg"
        };

        var uploadResult = await controller.UploadPhoto(formFile, ct: CancellationToken.None) as OkObjectResult;
        uploadResult.Should().NotBeNull();
        var photoId = (string?)uploadResult!.Value!.GetType().GetProperty("photoId")?.GetValue(uploadResult.Value!)!;
        photoId.Should().NotBeNullOrEmpty();

        var confirmRequest = new ConfirmDetectionsRequest
        {
            Confirmations = new [] { new DetectionConfirmation { DetectionId = "d1", UserInput = "3 whole" } }
        };

        var confirmResult = await controller.ConfirmDetections(photoId, confirmRequest, CancellationToken.None) as OkObjectResult;
        confirmResult.Should().NotBeNull();
        var itemsProp = confirmResult!.Value!.GetType().GetProperty("items");
        itemsProp.Should().NotBeNull();
    }
}
