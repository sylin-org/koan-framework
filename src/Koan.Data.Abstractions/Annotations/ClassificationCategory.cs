namespace Koan.Data.Abstractions.Annotations;

/// <summary>
/// A single data-classification category token — the FACT an entity declares about a property
/// (<c>[Pii]</c>, <c>[Phi]</c>, …) and against which solution/tenant policy later resolves HANDLING
/// (encrypt / tokenize / mask). The <see cref="Name"/> is a stable lowercase token so it serializes into
/// policy config and self-report payloads as-is.
/// <para>
/// The four well-known categories are statics; the raw-string constructor is the extension escape hatch
/// (an app may declare its own, e.g. <c>new("trade-secret")</c>) — classification is "one extensible axis,
/// not N hard-coded attributes" (tenancy-design §5a). Deliberately shaped like
/// <see cref="Koan.Core.Capabilities.Capability"/>. See ARCH-0098.
/// </para>
/// </summary>
public readonly record struct ClassificationCategory
{
    /// <summary>Creates a category from its stable lowercase token.</summary>
    public ClassificationCategory(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A classification category name must be a non-empty string.", nameof(name));
        Name = name;
    }

    /// <summary>The stable lowercase token, e.g. <c>"pii"</c>. Equality is by this value.</summary>
    public string Name { get; }

    /// <summary>Personally Identifiable Information — name, email, address, government id.</summary>
    public static readonly ClassificationCategory Pii = new("pii");

    /// <summary>Protected Health Information (HIPAA) — the strictest co-location / residency posture.</summary>
    public static readonly ClassificationCategory Phi = new("phi");

    /// <summary>Payment Card Industry data — PAN, CVV (PCI-DSS).</summary>
    public static readonly ClassificationCategory Pci = new("pci");

    /// <summary>A secret value — API keys, credentials. Write-only / masked-read (the strongest sensitivity).</summary>
    public static readonly ClassificationCategory Secret = new("secret");

    /// <inheritdoc />
    public override string ToString() => Name;
}
