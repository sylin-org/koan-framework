using System.ComponentModel;
using Koan.Data.Core.Model;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Koan.Web.Hooks;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Mcp.Verbs.Tests;

/// <summary>WEB-0087: a row whose controller serves what it declares, on REST and as tools.</summary>
public sealed class Notice : Entity<Notice>
{
    [Description("What the notice says.")]
    public string Text { get; set; } = "";

    /// <summary>Stamped on create from the board the create takes.</summary>
    public string Board { get; set; } = "";

    /// <summary>Stamped on create: who posted it.</summary>
    public string Author { get; set; } = "";
}

/// <summary>What posting a notice takes beside it.</summary>
public sealed record Posting([property: Description("The board it goes on; left out, the general board.")] string? Where = null);

/// <summary>Reads, posts and edits notices, and nothing else, on both doors.</summary>
[Route("/boards/notices")]
[Declared]
public sealed class NoticesController : EntityController<Notice>, IModelHook<Notice>
{
    [HttpGet("{notice}")]
    [Tool(Name = "read_notice")]
    [Description("Read a notice.")]
    public override Task<IActionResult> GetById(string notice, CancellationToken ct) => base.GetById(notice, ct);

    [Tool(Name = "post_notice")]
    [Description("Post a notice.")]
    [Writes(nameof(Notice.Id), nameof(Notice.Text))]
    [Input<Posting>]
    public override Task<IActionResult> Upsert(Notice model, CancellationToken ct) => base.Upsert(model, ct);

    [HttpPatch("{notice}")]
    [Tool(Name = "edit_notice")]
    [Description("Change what a notice says.")]
    [Writes(nameof(Notice.Text))]
    public override Task<IActionResult> Patch(string notice, CancellationToken ct) => base.Patch(notice, ct);

    public Task OnBeforeSave(HookContext<Notice> ctx, Notice model)
    {
        if (ctx.Prior is not null) return Task.CompletedTask;
        model.Board = ctx.Input<Posting>()?.Where ?? "general";
        model.Author = Forum.PersonId(ctx.User) ?? "";
        return Task.CompletedTask;
    }
}
