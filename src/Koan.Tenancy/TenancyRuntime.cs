using Koan.Core;
using Microsoft.Extensions.Options;

namespace Koan.Tenancy;

/// <summary>
/// The resolved tenancy runtime (ARCH-0099 §1) — computes the <see cref="TenancyPosture"/> once from the in-core
/// <see cref="KoanEnv.IsDevelopment"/> snapshot (snapshot-ambient-once) plus any explicit
/// <see cref="TenancyOptions.Posture"/> override, and holds it so the hot-path gate reads a field, not a
/// derivation. Registered as a singleton by the module; the fail-closed guard, the boot pre-flight, and the
/// boot report all read this one resolution so they never disagree.
/// </summary>
public sealed class TenancyRuntime
{
    /// <summary>The resolved posture — <see cref="TenancyPosture.Open"/> in dev, <see cref="TenancyPosture.Closed"/> otherwise.</summary>
    public TenancyPosture Posture { get; }

    public TenancyRuntime(IOptions<TenancyOptions> options)
    {
        var o = options?.Value ?? new TenancyOptions();
        Posture = TenancyPostureResolver.Resolve(KoanEnv.IsDevelopment, o.Posture);
    }
}
