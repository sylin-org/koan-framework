using Koan.Data.Core.Tenancy;

namespace Koan.Data.Core.Options;

/// <summary>
/// Tenancy posture for the data pillar (ARCH-0095). Bound from <c>Koan:Data:Tenancy</c>.
/// The tenancy kernel (P1–P3 + P7 at the <c>Koan.Data</c> level) is configured here; the multi-axis flow
/// (jobs/messaging/cache) lives above the "Magic Cliff" and is enabled by referencing those pillars.
/// </summary>
public sealed class TenancyOptions
{
    /// <summary>The activation gradient: <see cref="TenancyMode.Off"/> (default) → Warn → Enforce.</summary>
    public TenancyMode Mode { get; set; } = TenancyMode.Off;
}
