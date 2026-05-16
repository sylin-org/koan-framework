namespace Koan.AI.Contracts.Models;

/// <summary>Request for speech-to-text transcription (audio → text).</summary>
public sealed record TranscribeRequest
{
    /// <summary>Audio bytes to transcribe.</summary>
    public required byte[] Audio { get; init; }

    /// <summary>Model name (e.g., "whisper-large-v3", "recommended:transcribe").</summary>
    public string? Model { get; init; }

    /// <summary>Language hint (ISO 639-1, e.g., "en", "ja").</summary>
    public string? Language { get; init; }

    /// <summary>Desired output format.</summary>
    public TranscriptFormat Format { get; init; } = TranscriptFormat.PlainText;

    /// <summary>Enable speaker diarization.</summary>
    public bool? Diarize { get; init; }

    /// <summary>Include word-level timestamps.</summary>
    public bool? Timestamps { get; init; }

    /// <summary>Injected by router.</summary>
    public string? InternalConnectionString { get; set; }

    /// <summary>Pass-through vendor options.</summary>
    public IDictionary<string, object>? VendorOptions { get; init; }
}

/// <summary>Response from transcription.</summary>
public sealed record TranscribeResponse
{
    /// <summary>Full transcribed text.</summary>
    public required string Text { get; init; }

    /// <summary>Detected language (ISO 639-1).</summary>
    public string? Language { get; init; }

    /// <summary>Audio duration.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Model used.</summary>
    public string? Model { get; init; }

    /// <summary>Timed segments (when timestamps or SRT/VTT requested).</summary>
    public IReadOnlyList<TranscribeSegment>? Segments { get; init; }
}

/// <summary>A single transcription segment with timing.</summary>
public sealed record TranscribeSegment
{
    public required string Text { get; init; }
    public TimeSpan Start { get; init; }
    public TimeSpan End { get; init; }
    public string? Speaker { get; init; }
    public double? Confidence { get; init; }
}

/// <summary>Transcript output format.</summary>
public enum TranscriptFormat { PlainText, Srt, Vtt, Segments }
