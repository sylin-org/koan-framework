namespace Koan.Core;

/// <summary>
/// Neutral root for host-owned capability declarations inside <c>AddKoan(koan =&gt; ...)</c>.
/// Pillar packages contribute typed extension properties; the root itself carries no pillar machinery.
/// </summary>
public sealed class KoanApplicationBuilder
{
    internal KoanApplicationBuilder() { }
}
