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

/// <summary>A custom [McpTool] verb so the conformance specs cover the non-entity tool path too.</summary>
public static class ConformanceTools
{
    [McpTool(Name = "gadget_ping", Description = "Returns pong for the gadget tools.")]
    public static string GadgetPing(string echo) => $"pong:{echo}";
}
