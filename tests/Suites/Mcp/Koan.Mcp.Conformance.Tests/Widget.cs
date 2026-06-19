using System.ComponentModel.DataAnnotations;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Mcp;
using Microsoft.AspNetCore.Mvc;
using Koan.Web.Controllers;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>The colour enum exists so the AN11 did-you-mean spec has real schema facts (enum members)
/// to project at an error site — never row data.</summary>
public enum WidgetColor { Red, Green, Blue }

/// <summary>An entity exposed over MCP (Full) for the AN11 dry-run / state-delta / did-you-mean specs.
/// <list type="bullet">
/// <item><see cref="Title"/> is <c>[Required]</c> — drives the required-field did-you-mean.</item>
/// <item><see cref="Color"/> is an enum — drives the enum did-you-mean and shows as a delta transition.</item>
/// <item><see cref="Internal"/> is <c>[McpIgnore(Output)]</c> — must NEVER appear in a state delta
/// (walled-means-silent re-applied to the delta channel).</item>
/// </list></summary>
[McpEntity(Name = "widget", Description = "A widget", Exposure = McpExposureMode.Full)]
[StorageName("an11_widgets")]
public sealed class Widget : Entity<Widget>
{
    [Required]
    public string Title { get; set; } = "";

    public WidgetColor Color { get; set; }

    public int Stock { get; set; }

    [McpIgnore(McpFieldDirection.Output)]
    public string Internal { get; set; } = "";
}

[Route("api/widgets")]
public sealed class WidgetsController : EntityController<Widget>
{
}

/// <summary>A custom mutating verb whose effects live in imperative code the framework cannot inspect —
/// the AN11 / A10 case. A dry-run must NOT silently execute it; it returns an honest partial rehearsal.</summary>
public static class WidgetTools
{
    [McpTool(Name = "widget_recompute", Description = "Recomputes derived widget state (external effects).")]
    [McpDestructive]
    public static string Recompute(string id) => $"recomputed:{id}";
}
