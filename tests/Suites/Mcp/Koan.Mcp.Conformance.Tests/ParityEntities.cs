using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Mcp;
using Koan.Web.Authorization;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>SEC-0011: an entity that declares no access, so the ladder's default decision alone decides it.</summary>
[McpEntity(Name = "parity-plain", Exposure = McpExposureMode.Full)]
[StorageName("conformance_parity_plain")]
public sealed class ParityPlain : Entity<ParityPlain>
{
    public string Note { get; set; } = "";
}

/// <summary>SEC-0011: an entity whose read needs the editor role, which an agent grant can lend.</summary>
[McpEntity(Name = "parity-editor", Exposure = McpExposureMode.Full)]
[Access(read: "is:editor")]
[StorageName("conformance_parity_editor")]
public sealed class ParityEditor : Entity<ParityEditor>
{
    public string Note { get; set; } = "";
}
