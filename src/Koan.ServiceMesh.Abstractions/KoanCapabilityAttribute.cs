namespace Koan.ServiceMesh.Abstractions;

/// <summary>
/// Explicitly names a service capability method.
/// Use when method name doesn't match desired capability name.
/// Otherwise, capability is auto-detected from method name (converted to kebab-case).
/// </summary>
/// <example>
/// <code>
/// [KoanCapability("translate")]
/// public Task&lt;TranslationResult&gt; TranslateTextAsync(...) { }
/// // Capability: "translate" (not "translate-text")
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class KoanCapabilityAttribute : Attribute
{
    /// <summary>
    /// Capability name (e.g., "translate", "detect-language").
    /// Should be kebab-case by convention.
    /// </summary>
    public string CapabilityName { get; }

    public KoanCapabilityAttribute(string capabilityName)
    {
        CapabilityName = capabilityName ?? throw new ArgumentNullException(nameof(capabilityName));
    }
}
