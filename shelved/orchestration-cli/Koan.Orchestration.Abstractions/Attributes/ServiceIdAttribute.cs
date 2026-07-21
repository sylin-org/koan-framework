using System;

namespace Koan.Orchestration.Attributes;

/// <summary>
/// Declares a stable logical identifier for a service (e.g., "mongo").
/// </summary>
// ARCH-0077 (D2 item 2): RETAINED, not dead surface. No connector applies this today, but the type is still
// read by the manifest generator, the CLI ProjectDependencyAnalyzer, and ComposeExporter. It is retired with
// the orchestration→Aspire migration (ARCH-0077), not piecemeal. See docs/assessment/prompts/PROGRESS.md (D2).
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ServiceIdAttribute : Attribute
{
    public ServiceIdAttribute(string id)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
    }

    public string Id { get; }
}
