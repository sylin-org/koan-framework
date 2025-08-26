using Sora.Storage.Abstractions;

namespace Sora.Media.Abstractions.Model;

public interface IMediaObject : IStorageObject
{
    string? SourceMediaId { get; }
    string? RelationshipType { get; }
    string? DerivationKey { get; }
    string? ThumbnailMediaId { get; }
}