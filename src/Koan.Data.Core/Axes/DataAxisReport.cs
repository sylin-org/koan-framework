using System.Collections.Generic;
using System.Linq;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Pipeline;

namespace Koan.Data.Core.Axes;

/// <summary>
/// The boot-report self-projection of the active data-axis planes (ARCH-0101 §9) — the same self-reporting machinery
/// as the boot report, listing what data-segmentation is composed app-wide. DI-free (reads only the static registries),
/// so it runs in <c>KoanModule.Report</c> where no <c>IServiceProvider</c> is available. Per-entity detail (the active
/// fold + adapter satisfaction) lives in <see cref="DataAxis.Explain"/>.
/// </summary>
public static class DataAxisReport
{
    /// <summary>A one-line summary of the registered axis planes, or <c>null</c> when none is registered (the boot
    /// report omits the line entirely — off = structurally absent).</summary>
    public static string? Summarize()
    {
        var fields = ManagedFieldRegistry.All;
        var hasOverrides = !OperationOverrideRegistry.IsEmpty;
        var hasParticles = !StorageNameParticleRegistry.IsEmpty;
        if (fields.Count == 0 && !hasOverrides && !hasParticles) return null;

        var parts = new List<string>();
        if (fields.Count > 0)
            parts.Add("fields=[" + string.Join(", ", fields.Select(f =>
                $"{f.StorageName}:{(f.RequiredCapability is { } c ? c.Id : "none")}{(f.AutoReadFilter ? "" : "/predicate")}")) + "]");
        if (hasOverrides) parts.Add("operation-overrides=on");
        if (hasParticles) parts.Add("container-particles=on");
        return string.Join("; ", parts);
    }
}
