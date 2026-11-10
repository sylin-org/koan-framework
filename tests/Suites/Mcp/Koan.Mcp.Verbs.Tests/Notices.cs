using System.ComponentModel;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core.Model;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Koan.Web.Endpoints;
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

    /// <summary>Stamped on every write: the operation its hook was told it served (WEB-0089).</summary>
    public string Told { get; set; } = "";
}

/// <summary>The page a refused form of a notice reads again (WEB-0089).</summary>
public sealed record NoticeFormPage(string Operation, string Word);

/// <summary>What posting a notice takes beside it.</summary>
public sealed record Posting([property: Description("The board it goes on; left out, the general board.")] string? Where = null);

/// <summary>Which board a list of notices reads.</summary>
public sealed record Listing([property: Description("The board to read.")] string? On = null);

/// <summary>Lists, reads, posts and edits notices, and nothing else, on both doors, and hands a refused form its page.</summary>
[Route("/boards/notices")]
[Declared]
public sealed class NoticesController : EntityController<Notice>, IModelHook<Notice>, IRequestOptionsHook<Notice>
{
    [HttpGet("")]
    [Tool(Name = "read_notices")]
    [Description("Read a board's notices, by what they say.")]
    // A MaxSize below the DefaultSize is raised to it, so a page holds at most 2: what the tool states and REST serves alike.
    [Pagination(DefaultSize = 2, MaxSize = 1, DefaultSort = nameof(Notice.Text))]
    [Input<Listing>]
    public override Task<IActionResult> GetCollection(CancellationToken ct) => base.GetCollection(ct);

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

    public Task OnBuildingOptions(HookContext<Notice> ctx, QueryOptions opts)
    {
        // Only the list takes a board; a read by id finds its notice wherever it is.
        if (ctx.Input<Listing>() is { } listing) opts.Filter = Filter.And(opts.Filter, Filter.Eq(nameof(Notice.Board), listing.On ?? "general"));
        return Task.CompletedTask;
    }

    public Task OnBeforeSave(HookContext<Notice> ctx, Notice model)
    {
        model.Told = ctx.Operation.ToString();
        if (ctx.Prior is not null) return Task.CompletedTask;
        model.Board = ctx.Input<Posting>()?.Where ?? "general";
        model.Author = Forum.PersonId(ctx.User) ?? "";
        return Task.CompletedTask;
    }

    protected override ValueTask<object?> RefusedPage(EntityEndpointOperationKind operation, Problem refused, CancellationToken ct)
        => ValueTask.FromResult<object?>(new NoticeFormPage(operation.ToString(), refused.Word));
}
