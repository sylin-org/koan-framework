namespace Koan.Orchestration.Attributes;

/// <summary>
/// Declares default app environment variables. Provide as KEY=VALUE pairs.
/// Values can use tokens: {scheme},{host},{port},{serviceId} resolved against the selected mode.
/// </summary>
// ARCH-0077 (D2 item 2): RETAINED, not dead surface. No connector applies this today, but the type is still
// read by the manifest generator, the CLI ProjectDependencyAnalyzer, and ComposeExporter. It is retired with
// the orchestration→Aspire migration (ARCH-0077), not piecemeal. See docs/assessment/prompts/PROGRESS.md (D2).
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class AppEnvDefaultsAttribute : Attribute
{
    public AppEnvDefaultsAttribute(params string[] pairs)
    {
        Pairs = pairs ?? [];
    }

    public string[] Pairs { get; }
}