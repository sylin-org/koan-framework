namespace Koan.Orchestration.Attributes;

/// <summary>
/// Declares default app environment variables. Provide as KEY=VALUE pairs.
/// Values can use tokens: {scheme},{host},{port},{serviceId} resolved against the selected mode.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class AppEnvDefaultsAttribute : Attribute
{
    public AppEnvDefaultsAttribute(params string[] pairs)
    {
        Pairs = pairs ?? Array.Empty<string>();
    }

    public string[] Pairs { get; }
}