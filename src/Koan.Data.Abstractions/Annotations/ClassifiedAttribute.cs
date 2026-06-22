namespace Koan.Data.Abstractions.Annotations;

/// <summary>
/// Declares a FACT: this property holds data of a given classification <see cref="Category"/> (PII / PHI / …).
/// When a handling module (<c>Koan.Classification</c>) is referenced and policy assigns a handling, the framework
/// transforms the value (encrypt / tokenize / mask) at the data-core write/read chokepoint. The entity declares
/// the fact ONLY — no handling lives on the entity (conformity-by-design: a data-core <c>grep -i "pii"</c> stays
/// empty; handling is config + the module). <see cref="PiiAttribute"/> / <see cref="PhiAttribute"/> /
/// <see cref="PciAttribute"/> / <see cref="SecretAttribute"/> are sugar over this extensible primitive. ARCH-0098 §2.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class ClassifiedAttribute : Attribute
{
    /// <summary>Declares the property's classification category by its stable token (e.g. <c>"pii"</c>).</summary>
    public ClassifiedAttribute(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("A classification category must be a non-empty string.", nameof(category));
        Category = new ClassificationCategory(category);
    }

    /// <summary>The declared category fact.</summary>
    public ClassificationCategory Category { get; }

    /// <summary>
    /// When true, the field supports blind-equality search — a deterministic keyed-HMAC comparand pushes equality
    /// down while LIKE/range stay honestly denied (ARCH-0098 §0 reserved exception). Opt-in; default false.
    /// </summary>
    public bool Searchable { get; set; }
}

/// <summary>Sugar for <c>[Classified("pii")]</c> — Personally Identifiable Information.</summary>
public sealed class PiiAttribute : ClassifiedAttribute
{
    public PiiAttribute() : base("pii") { }
}

/// <summary>Sugar for <c>[Classified("phi")]</c> — Protected Health Information (HIPAA).</summary>
public sealed class PhiAttribute : ClassifiedAttribute
{
    public PhiAttribute() : base("phi") { }
}

/// <summary>Sugar for <c>[Classified("pci")]</c> — Payment Card Industry data (PCI-DSS).</summary>
public sealed class PciAttribute : ClassifiedAttribute
{
    public PciAttribute() : base("pci") { }
}

/// <summary>Sugar for <c>[Classified("secret")]</c> — a secret value (write-only / masked-read).</summary>
public sealed class SecretAttribute : ClassifiedAttribute
{
    public SecretAttribute() : base("secret") { }
}
