using System;
using System.Collections.Generic;
using System.Linq;

namespace Koan.Tenancy;

/// <summary>
/// Thrown at boot when the tenancy pre-flight (ARCH-0099 §1) finds one or more hard failures in Production —
/// the host refuses to boot rather than start in a state where tenant isolation is silently absent. The message
/// names every failure and the exact fix (Redis protected-mode quality), so the operator sees what to add.
/// </summary>
public sealed class TenancyBootException : Exception
{
    /// <summary>The individual hard failures, each naming its fix.</summary>
    public IReadOnlyList<string> Failures { get; }

    public TenancyBootException(IReadOnlyList<string> failures)
        : base(Compose(failures))
    {
        Failures = failures;
    }

    private static string Compose(IReadOnlyList<string> failures)
        => "Tenancy refused to boot (ARCH-0099 §1) — fix the following before starting in Production:"
           + Environment.NewLine
           + string.Join(Environment.NewLine, (failures ?? Array.Empty<string>()).Select(f => "  - " + f));
}
