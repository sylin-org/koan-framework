namespace Koan.AI.Contracts.Options;

/// <summary>Options for translation requests.</summary>
public sealed record TranslateOptions
{
    /// <summary>Target language (ISO 639-1). Required.</summary>
    public required string Target { get; init; }

    /// <summary>Source language hint. Auto-detected if omitted.</summary>
    public string? SourceLanguage { get; init; }

    /// <summary>Translation tone.</summary>
    public TranslateTone? Tone { get; init; }

    /// <summary>Override source for this request.</summary>
    public string? Source { get; init; }

    /// <summary>Override model for this request.</summary>
    public string? Model { get; init; }

    /// <summary>Vendor-specific options.</summary>
    public IDictionary<string, object>? VendorOptions { get; init; }
}

/// <summary>Translation tone.</summary>
public enum TranslateTone { Formal, Casual, Technical }
