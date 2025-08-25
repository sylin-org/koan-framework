namespace Sora.Storage.Abstractions;

public record StorageProviderCapabilities(
    bool SupportsSequentialRead,
    bool SupportsSeek,
    bool SupportsPresignedRead,
    bool SupportsServerSideCopy
);