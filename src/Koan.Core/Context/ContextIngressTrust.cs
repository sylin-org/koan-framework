namespace Koan.Core.Context;

/// <summary>
/// Describes how strongly an ingress proves the origin and integrity of carried Koan context.
/// This is provenance only; it does not imply confidentiality, authorization, delivery, or payload correctness.
/// </summary>
public enum ContextIngressTrust
{
    /// <summary>No verified application origin or envelope integrity.</summary>
    Unverified = 0,

    /// <summary>The ingress authenticated the intended application/mesh and verified envelope integrity.</summary>
    Authenticated = 1,

    /// <summary>The context stayed inside the application's administrative trust boundary.</summary>
    HostTrusted = 2
}
