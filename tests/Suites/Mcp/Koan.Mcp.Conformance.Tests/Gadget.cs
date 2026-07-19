using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Mcp;
using Microsoft.AspNetCore.Mvc;
using Koan.Web.Controllers;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>An entity exposed over MCP (Full) so the wire-shape/annotation conformance specs have
/// entity tools to inspect.</summary>
[McpEntity(Name = "gadget", Description = "A gadget", Exposure = McpExposureMode.Full)]
[StorageName("conformance_gadgets")]
public sealed class Gadget : Entity<Gadget>
{
    public string Name { get; set; } = "";

    public int Quantity { get; set; }
}

[Route("api/gadgets")]
public sealed class GadgetsController : EntityController<Gadget>
{
}

/// <summary>Custom [McpTool] verbs so the conformance specs cover the non-entity tool path too.</summary>
public static class ConformanceTools
{
    // Unmarked: AN4 emits no hints for it (custom verbs gain nothing automatically).
    [McpTool(Name = "gadget_ping", Description = "Returns pong for the gadget tools.")]
    public static string GadgetPing(string echo) => $"pong:{echo}";

    // Explicitly marked destructive (AN4 opt-in) — the dangerous hand-written verb that must be annotated.
    [McpTool(Name = "gadget_purge", Description = "Permanently purges gadgets.")]
    [McpDestructive]
    public static string GadgetPurge(string confirm) => $"purged:{confirm}";

    [McpTool(
        Name = "gadget_admin",
        Description = "Performs a scope-protected gadget operation.",
        RequiredScopes = ["gadget:admin"])]
    public static string GadgetAdmin() => "admin";

    [McpTool(Name = "gadget_shape", Description = "Returns a structured gadget result.")]
    public static GadgetShape GadgetShape(GadgetShape request)
        => request with { InternalNote = "must-not-cross-the-wire" };
}

public sealed record GadgetShape(string DisplayName, int ItemCount)
{
    [McpIgnore(McpFieldDirection.Output)]
    public string InternalNote { get; init; } = "";
}
