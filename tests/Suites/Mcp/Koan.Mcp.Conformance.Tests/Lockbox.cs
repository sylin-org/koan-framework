using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Mcp;
using Koan.Web.Authorization;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>
/// SEC-0004 origin — an entity whose READ is gated by transport origin, not identity: <c>[Access(read:
/// "origin:local")]</c> admits only a call that demonstrably arrived over STDIO (the framework-stamped
/// <c>koan:origin=local</c> claim), regardless of who the caller is. Write is open, so it can be seeded from any
/// origin. Proves the headline: a STDIO/local agent may read it; a remote (HTTP/SSE or default) caller may not.
/// </summary>
[McpEntity(Name = "lockbox", Exposure = McpExposureMode.Full)]
[Access(read: "origin:local")]
[StorageName("conformance_lockboxes")]
public sealed class Lockbox : Entity<Lockbox>
{
    public string Contents { get; set; } = "";
}
