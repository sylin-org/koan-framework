using System;

namespace Koan.AI.Pipelines;

/// <summary>
/// Result of storing content (image, text, etc.) in storage.
/// </summary>
public sealed record StorageResult
{
    /// <summary>
    /// Storage identifier (GUID v7).
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Size in bytes of stored content.
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>
    /// MIME type of stored content.
    /// </summary>
    public string MimeType { get; init; } = "application/octet-stream";

    /// <summary>
    /// Optional storage container/bucket name.
    /// </summary>
    public string? Container { get; init; }

    /// <summary>
    /// Optional URL to access the stored content.
    /// </summary>
    public string? Url { get; init; }
}
