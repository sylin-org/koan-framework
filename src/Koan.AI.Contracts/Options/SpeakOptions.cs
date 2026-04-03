namespace Koan.AI.Contracts.Options;

/// <summary>Options for text-to-speech synthesis requests.</summary>
public sealed record SpeakOptions
{
    /// <summary>Voice identifier or name.</summary>
    public string? Voice { get; init; }

    /// <summary>Speech speed (0.5–2.0).</summary>
    public double? Speed { get; init; }

    /// <summary>Desired output audio format.</summary>
    public Models.AudioFormat? Format { get; init; }

    /// <summary>Language hint.</summary>
    public string? Language { get; init; }

    /// <summary>Emotion/style hint (provider-dependent).</summary>
    public string? Style { get; init; }

    /// <summary>Override source for this request.</summary>
    public string? Source { get; init; }

    /// <summary>Override model for this request.</summary>
    public string? Model { get; init; }

    /// <summary>Vendor-specific options.</summary>
    public IDictionary<string, object>? VendorOptions { get; init; }
}
