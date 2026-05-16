namespace Koan.AI.Contracts.Models;

/// <summary>Request for text-to-speech synthesis (text → audio).</summary>
public sealed record SpeakRequest
{
    /// <summary>Text to synthesize.</summary>
    public required string Text { get; init; }

    /// <summary>Model name (e.g., "tts-1-hd", "recommended:speak").</summary>
    public string? Model { get; init; }

    /// <summary>Voice identifier or name.</summary>
    public string? Voice { get; init; }

    /// <summary>Speech speed (0.5–2.0).</summary>
    public double? Speed { get; init; }

    /// <summary>Desired output audio format.</summary>
    public AudioFormat Format { get; init; } = AudioFormat.Mp3;

    /// <summary>Language hint.</summary>
    public string? Language { get; init; }

    /// <summary>Emotion/style hint (provider-dependent).</summary>
    public string? Style { get; init; }

    /// <summary>Injected by router.</summary>
    public string? InternalConnectionString { get; set; }

    /// <summary>Pass-through vendor options.</summary>
    public IDictionary<string, object>? VendorOptions { get; init; }
}

/// <summary>Response from text-to-speech.</summary>
public sealed record SpeakResponse
{
    /// <summary>Generated audio bytes.</summary>
    public required byte[] Audio { get; init; }

    /// <summary>Actual audio format.</summary>
    public AudioFormat Format { get; init; }

    /// <summary>Model used.</summary>
    public string? Model { get; init; }

    /// <summary>Voice used.</summary>
    public string? Voice { get; init; }

    /// <summary>Audio duration.</summary>
    public TimeSpan? Duration { get; init; }
}

/// <summary>Output audio format.</summary>
public enum AudioFormat { Mp3, Wav, Ogg, Flac }
