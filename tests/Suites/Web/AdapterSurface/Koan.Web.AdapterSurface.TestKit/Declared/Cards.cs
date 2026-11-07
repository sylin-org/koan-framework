using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Web.Attributes;
using Koan.Web.Authorization;
using Koan.Web.Controllers;
using Koan.Web.Hooks;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Web.AdapterSurface.TestKit.Declared;

/// <summary>WEB-0087: a row four controllers serve, two declared, one a declared singleton, one the whole surface.</summary>
[StorageName("cards_surface")]
public sealed class Card : Entity<Card>
{
    public string Title { get; set; } = "";

    /// <summary>Stamped by <see cref="CardsController"/> on create: the address a card also answers at.</summary>
    public string Slug { get; set; } = "";

    /// <summary>Stamped by <see cref="CardsController"/> on create, from its input.</summary>
    public string Shelf { get; set; } = "";

    /// <summary>Counted by <see cref="CardsController"/> on every change.</summary>
    public int Edits { get; set; }
}

/// <summary>What a create takes beside the card.</summary>
public sealed record CardPlacing(string? Place = null, int? Copies = null);

/// <summary>Serves a card by its id or its slug, creates one with the title it declares and the place it takes, changes only its title, and
/// lets only someone signed in remove one.</summary>
[Route("api/cards")]
[Declared]
public sealed class CardsController : EntityController<Card>, IModelHook<Card>
{
    [HttpGet("by/{card}")]
    public override Task<IActionResult> GetById(string card, CancellationToken ct) => base.GetById(card, ct);

    [Writes(nameof(Card.Id), nameof(Card.Title))]
    [Input<CardPlacing>]
    public override Task<IActionResult> Upsert(Card model, CancellationToken ct) => base.Upsert(model, ct);

    [HttpPatch("by/{card}")]
    [Writes(nameof(Card.Title))]
    public override Task<IActionResult> Patch(string card, CancellationToken ct) => base.Patch(card, ct);

    [HttpDelete("by/{card}")]
    [Access(remove: "authenticated")]
    public override Task<IActionResult> Delete(string card, CancellationToken ct) => base.Delete(card, ct);

    protected override async ValueTask<string?> KeyOf(string segment, CancellationToken ct)
        => (await Card.Query(card => card.Slug == segment, ct)).FirstOrDefault()?.Id ?? segment;

    /// <summary>WEB-0088: a card answers at its slug, so that is where a write of one is sent on to.</summary>
    protected override string AddressOf(Card row) => "/api/cards/by/" + Uri.EscapeDataString(row.Slug);

    public Task OnBeforeSave(HookContext<Card> ctx, Card model)
    {
        if (ctx.Prior is { } prior) model.Edits = prior.Edits + 1;
        else
        {
            var placing = ctx.Input<CardPlacing>();
            model.Shelf = placing?.Place ?? "loose";
            model.Slug = model.Title.ToLowerInvariant().Replace(' ', '-');
        }
        return Task.CompletedTask;
    }
}

/// <summary>The one front card, at the controller's own address.</summary>
[Route("api/front-card")]
[Declared]
public sealed class FrontCardController : EntityController<Card>
{
    public const string Front = "front";

    [HttpGet("")]
    public override Task<IActionResult> GetById(string id, CancellationToken ct) => base.GetById(id, ct);

    protected override ValueTask<string?> KeyOf(string segment, CancellationToken ct) => ValueTask.FromResult<string?>(Front);
}

/// <summary>Creates a card, and answers a create sent again with the same title as the first one was answered, with the card it made.</summary>
[Route("api/card-replays")]
[Declared]
public sealed class CardReplaysController : EntityController<Card>, IModelHook<Card>
{
    [Writes(nameof(Card.Id), nameof(Card.Title))]
    public override Task<IActionResult> Upsert(Card model, CancellationToken ct) => base.Upsert(model, ct);

    public Task OnBeforeSave(HookContext<Card> ctx, Card model)
    {
        if (ctx.Prior is { } prior && prior.Title == model.Title) ctx.ShortCircuit(prior);
        return Task.CompletedTask;
    }
}

/// <summary>What a delete takes beside the card: how many edits its sender read.</summary>
public sealed record CardLeaving(int? Seen = null);

/// <summary>Creates, changes and deletes cards at the addresses it leaves alone, a delete only of a card as its sender read it.</summary>
[Route("api/loose-cards")]
[Declared]
public sealed class LooseCardsController : EntityController<Card>, IModelHook<Card>
{
    [Writes(nameof(Card.Id), nameof(Card.Title))]
    public override Task<IActionResult> Upsert(Card model, CancellationToken ct) => base.Upsert(model, ct);

    [HttpPatch("{card}")]
    [Writes(nameof(Card.Title), nameof(Card.Edits))]
    public override Task<IActionResult> Patch(string card, CancellationToken ct) => base.Patch(card, ct);

    [HttpDelete("{card}")]
    [Input<CardLeaving>]
    public override Task<IActionResult> Delete(string card, CancellationToken ct) => base.Delete(card, ct);

    public Task OnBeforeDelete(HookContext<Card> ctx, Card model)
    {
        if (ctx.Input<CardLeaving>()?.Seen != model.Edits)
            throw new Koan.Data.Core.Commands.Refusal(Koan.Data.Core.Commands.Reason.Conflict("edited", "This card was edited since it was read."));
        return Task.CompletedTask;
    }
}

/// <summary>A verb beside the cards, answered as an entity write is (WEB-0088).</summary>
[ApiController]
[Route("api/card-verbs")]
public sealed class CardVerbsController : ControllerBase
{
    [HttpPost("{card}/pin")]
    public IActionResult Pin(string card) => this.Written("/api/cards/by/" + Uri.EscapeDataString(card), new { card, pinned = true }, created: false);

    /// <summary>Names its address with the card as it was routed, unescaped, as a header cannot carry it.</summary>
    [HttpPost("{card}/flag")]
    public IActionResult Flag(string card) => this.Written("/api/cards/by/" + card, new { card, flagged = true }, created: false);
}

/// <summary>The whole surface over the same row, with no hook of its own.</summary>
[Route("api/plain-cards")]
public sealed class PlainCardsController : EntityController<Card>;
