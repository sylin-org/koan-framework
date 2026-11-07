using System.ComponentModel;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core.Model;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Koan.Web.Hooks;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Web.AdapterSurface.TestKit.Declared;

/// <summary>DATA-0170: a row two declared collections list, one by its label and one newest first.</summary>
[StorageName("pins_surface")]
public sealed class Pin : Entity<Pin>
{
    public string Label { get; set; } = "";

    public DateTimeOffset PinnedAt { get; set; }

    /// <summary>The board a pin is on, which keeps each spec's rows to itself.</summary>
    public string Board { get; set; } = "";
}

/// <summary>Which board a list reads.</summary>
public sealed record PinBoard([property: Description("The board to list.")] string? On = null);

/// <summary>Lists one board's pins by label, two to a page and three at most.</summary>
[Route("api/pins/by-label")]
[Declared]
public sealed class PinsByLabelController : EntityController<Pin>, IRequestOptionsHook<Pin>
{
    [HttpGet("")]
    [Pagination(DefaultSize = 2, MaxSize = 3, DefaultSort = nameof(Pin.Label))]
    [Input<PinBoard>]
    public override Task<IActionResult> GetCollection(CancellationToken ct) => base.GetCollection(ct);

    public Task OnBuildingOptions(HookContext<Pin> ctx, QueryOptions opts)
    {
        opts.Filter = Filter.And(opts.Filter, Filter.Eq(nameof(Pin.Board), ctx.Input<PinBoard>()?.On ?? ""));
        return Task.CompletedTask;
    }
}

/// <summary>Lists one board's pins newest first, and composes its own page, which links the next one.</summary>
[Route("api/pins/newest")]
[Declared]
public sealed class NewestPinsController : EntityController<Pin>, IRequestOptionsHook<Pin>, IEmitHook<Pin>
{
    [HttpGet("")]
    [Pagination(DefaultSize = 2, MaxSize = 3, DefaultSort = "-" + nameof(Pin.PinnedAt))]
    [Input<PinBoard>]
    public override Task<IActionResult> GetCollection(CancellationToken ct) => base.GetCollection(ct);

    public Task OnBuildingOptions(HookContext<Pin> ctx, QueryOptions opts)
    {
        opts.Filter = Filter.And(opts.Filter, Filter.Eq(nameof(Pin.Board), ctx.Input<PinBoard>()?.On ?? ""));
        return Task.CompletedTask;
    }

    public Task<EmitDecision> OnEmitCollection(HookContext<Pin> ctx, object payload)
        => Task.FromResult<EmitDecision>(EmitDecision.With(new PinPage(
            ((IEnumerable<Pin>)payload).Select(pin => pin.Label).ToArray(),
            ctx.Next is { } next ? $"/api/pins/newest?on={ctx.Input<PinBoard>()?.On}&after={Uri.EscapeDataString(next)}" : null)));
}

/// <summary>The page <see cref="NewestPinsController"/> composes.</summary>
public sealed record PinPage(IReadOnlyList<string> Labels, string? Older);
