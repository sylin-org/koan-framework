using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using S16.PantryPal.Controllers;
using S16.PantryPal.Services;
using S16.PantryPal.Models;
using S16.PantryPal.Contracts;
using Koan.Data.Core.Model;

namespace S16.PantryPal.Tests;

[Collection("KoanHost")]
public class IngestionFlowTests
{
    private sealed class FakePhotoStorage : IPhotoStorage
    {
        private readonly Dictionary<string, byte[]> _store = new();
        public Task<string> StoreAsync(Stream content, string originalFileName, string? contentType, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            content.CopyTo(ms);
            var key = $"photos/{Guid.NewGuid()}{Path.GetExtension(originalFileName)}";
            _store[key] = ms.ToArray();
            return Task.FromResult(key);
        }
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
            => Task.FromResult<Stream>(new MemoryStream(_store[key]));
    }
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
                    new PantryDetection
                    {
                        Id = "d1",
                        Candidates = new [] { new DetectionCandidate { Id = "c1", Name = "banana", Confidence = 0.88f, DefaultUnit = "whole" } },
                        ParsedData = new ParsedItemData { Quantity = 1, Unit = "whole" }
                    }
                }
            });
        }

        public Task LearnFromCorrectionAsync(string originalName, string correctedName, string? correctedQuantity, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeParser : IPantryInputParser
    {
    public ParsedItemData ParseInput(string input, ParserContext? context = null) => new ParsedItemData { Quantity = 3, Unit = "whole", ExpiresAt = DateTime.UtcNow.AddDays(4) };
    }

    [Fact]
    public async Task Upload_And_Confirm_Should_Create_PantryItem()
    {
        var vision = new FakeVision();
        var parser = new FakeParser();
        var confirm = new PantryConfirmationService();

        var photoStorage = new FakePhotoStorage();
        var controller = new PantryIngestionController(vision, parser, confirm, photoStorage)
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

        var confirmRequest = new S16.PantryPal.Contracts.ConfirmDetectionsRequest
        {
            Confirmations = new [] { new S16.PantryPal.Contracts.DetectionConfirmation { DetectionId = "d1", UserInput = "3 whole" } }
        };

        var confirmResult = await controller.ConfirmDetections(photoId, confirmRequest, CancellationToken.None) as OkObjectResult;
        confirmResult.Should().NotBeNull();
        var itemsProp = confirmResult!.Value!.GetType().GetProperty("items");
        itemsProp.Should().NotBeNull();
    }
}
