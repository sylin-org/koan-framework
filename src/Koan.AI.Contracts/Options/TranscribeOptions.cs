namespace Koan.AI.Contracts.Options;

/// <summary>Options for speech-to-text transcription requests.</summary>
public sealed record TranscribeOptions
{
    /// <summary>Language hint (ISO 639-1, e.g., "en", "ja").</summary>
    public string? Language { get; init; }

    /// <summary>Desired output format.</summary>
    public Models.TranscriptFormat? Format { get; init; }

    /// <summary>Enable speaker diarization.</summary>
    public bool? Diarize { get; init; }

    /// <summary>Include word-level timestamps.</summary>
    public bool? Timestamps { get; init; }

    /// <summary>Override source for this request.</summary>
    public string? Source { get; init; }

    /// <summary>Override model for this request.</summary>
    public string? Model { get; init; }

    /// <summary>Vendor-specific options.</summary>
    public IDictionary<string, object>? VendorOptions { get; init; }
}
