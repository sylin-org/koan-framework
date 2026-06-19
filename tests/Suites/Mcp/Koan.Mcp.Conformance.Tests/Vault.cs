using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Mcp;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>A scoped entity — its tools require the <c>vault:read</c> scope. Used to prove the shared
/// enforcement policy denies an unscoped caller while STDIO (raw handler) stays local-trust.</summary>
[McpEntity(Name = "vault", Description = "A scoped vault", Exposure = McpExposureMode.Full, RequiredScopes = new[] { "vault:read" })]
[StorageName("conformance_vaults")]
public sealed class Vault : Entity<Vault>
{
    public string Secret { get; set; } = "";
}
