using Koan.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Tenancy;

/// <summary>
/// The resolved tenancy runtime (ARCH-0099 §1) — computes the <see cref="TenancyPosture"/> once, per host, from
/// the <b>per-host</b> <see cref="IHostEnvironment"/> (through <see cref="Koan.Core.KoanEnv.Gate.DevelopmentOnly"/>,
/// so an open posture obeys the same rule as every other development-only surface) plus any explicit
/// <see cref="TenancyOptions.Posture"/>
/// override, and holds it so the hot-path gate reads a field, not a derivation.
///
/// <para>It is deliberately <b>per-host</b>, not the process-global <c>KoanEnv</c> snapshot: <c>KoanEnv</c> latches
/// to whichever host initialises it first, so in a multi-host process a Production host could inherit a
/// Development snapshot. The fail-closed gate and the boot pre-flight read this one resolution; the pre-flight
/// enforces the invariant "Open is legal only in Development", which a per-host source guarantees can never be
/// defeated by a process-init-order latch.</para>
/// </summary>
public sealed class TenancyRuntime
{
    /// <summary>The resolved posture — <see cref="TenancyPosture.Open"/> in dev, <see cref="TenancyPosture.Closed"/> otherwise.</summary>
    public TenancyPosture Posture { get; }

    public TenancyRuntime(IOptions<TenancyOptions> options, IHostEnvironment environment)
    {
        var o = options?.Value ?? new TenancyOptions();
        Posture = TenancyPostureResolver.Resolve(KoanEnv.Gate.DevelopmentOnly(environment), o.Posture);
    }
}
