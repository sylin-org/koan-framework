using System;
using System.Threading;
using Koan.Core;
using Koan.Core.Semantics.Contributions;
using Koan.Core.Semantics.Segmentation;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Koan.Data.AdapterSurface.TestKit;

/// <summary>
/// A row-scoped <c>tenant</c> dimension contributed exactly as <c>Koan.Tenancy</c> contributes its own (one hard
/// segmentation dimension, realized by Data as the managed field <c>__koan_tenant</c>), for
/// <see cref="ConformanceMember"/> only, and only in a host composed while <see cref="ConformanceTenancy.Segmented"/> is
/// set. The connector suites do not reference <c>Koan.Tenancy</c>, so this is how they run the
/// DATA-0150 specs under row-scoped tenancy without scoping every other entity they use.
/// </summary>
public sealed class ConformanceTenancyModule : KoanModule, IContributeTo<SegmentationContributionTarget>
{
    // Contributed only to a host composed while a spec asks for it: a hard dimension makes every Direct session and
    // registered operation require a tenant, which the other specs in a connector suite do not supply.
    public void Contribute(SegmentationContributionTarget target)
    {
        if (!ConformanceTenancy.Segmented) return;
        target.Require(
        ConformanceTenancy.DimensionId,
        static () => ConformanceTenancy.Current is { Length: > 0 } tenant
            ? SegmentationValue.For(tenant)
            : SegmentationValue.Missing,
        static type => type == typeof(ConformanceMember),
        "Run the ConformanceMember operation inside ConformanceTenancy.Use(...).");
    }
}

/// <summary>The ambient conformance tenant, and whether <see cref="ConformanceMember"/> is tenant-scoped.</summary>
public static class ConformanceTenancy
{
    public const string DimensionId = "tenant";

    private static readonly AsyncLocal<string?> Tenant = new();

    /// <summary>
    /// Whether a host composed now has the tenant dimension. Off, the host plans <see cref="ConformanceMember"/>'s
    /// unique index as every Koan before DATA-0150 planned it: global. A spec sets it before it boots a host.
    /// </summary>
    public static bool Segmented { get; set; }

    public static string? Current => Tenant.Value;

    public static IDisposable Use(string tenant)
    {
        var previous = Tenant.Value;
        Tenant.Value = tenant;
        return new Scope(previous);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        public void Dispose() => Tenant.Value = previous;
    }
}

/// <summary>The DATA-0150 entity: one unique member, as an application would declare it.</summary>
public sealed class ConformanceMember : Entity<ConformanceMember>
{
    [Index(Unique = true)]
    public string Email { get; set; } = "";
}
