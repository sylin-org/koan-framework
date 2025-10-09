using Koan.Data.Core;
using S16.PantryPal.Models;
using S16.PantryPal.Contracts;

namespace S16.PantryPal.Services;

public interface IPantryConfirmationService
{
    Task<IReadOnlyList<PantryItem>> ConfirmDetectionsAsync(string photoId, IEnumerable<DetectionConfirmation> confirmations, IPantryVisionService visionService, IPantryInputParser parser, CancellationToken ct = default);
}

public sealed class PantryConfirmationService : IPantryConfirmationService
{
    public async Task<IReadOnlyList<PantryItem>> ConfirmDetectionsAsync(string photoId, IEnumerable<DetectionConfirmation> confirmations, IPantryVisionService visionService, IPantryInputParser parser, CancellationToken ct = default)
    {
        var photo = await PantryPhoto.Get(photoId);
        if (photo == null) throw new InvalidOperationException("Photo not found");

        var confirmedItems = new List<PantryItem>();
        var confirmationList = confirmations?.ToList() ?? new List<DetectionConfirmation>();

        foreach (var confirmation in confirmationList)
        {
            var detection = photo.Detections.FirstOrDefault(d => d.Id == confirmation.DetectionId);
            if (detection == null) continue;

            ParsedItemData? parsedData = null;
            if (!string.IsNullOrWhiteSpace(confirmation.UserInput))
            {
                parsedData = parser.ParseInput(confirmation.UserInput);
            }

            var candidate = detection.Candidates.FirstOrDefault(c =>
                c.Id == (confirmation.SelectedCandidateId ?? detection.SelectedCandidateId))
                ?? detection.Candidates.FirstOrDefault();

            if (candidate == null) continue;

            var item = new PantryItem
            {
                Name = candidate.Name,
                Category = candidate.Category ?? "uncategorized",
                Quantity = parsedData?.Quantity ?? detection.ParsedData?.Quantity ?? 1,
                Unit = parsedData?.Unit ?? detection.ParsedData?.Unit ?? candidate.DefaultUnit ?? "whole",
                ExpiresAt = parsedData?.ExpiresAt ?? detection.ParsedData?.ExpiresAt,
                AddedAt = DateTime.UtcNow,
                Status = "available",
                VisionMetadata = new VisionMetadata
                {
                    SourcePhotoId = photoId,
                    DetectionId = detection.Id,
                    Confidence = candidate.Confidence,
                    WasUserCorrected = !string.IsNullOrWhiteSpace(confirmation.UserInput)
                }
            };

            await item.Save();
            confirmedItems.Add(item);

            if (!string.IsNullOrWhiteSpace(confirmation.UserInput))
            {
                await visionService.LearnFromCorrectionAsync(candidate.Name, item.Name, confirmation.UserInput!, ct);
            }

            detection.Status = "confirmed";
        }

        photo.ItemsConfirmed = confirmedItems.Count;
        await photo.Save();

        return confirmedItems;
    }
}
