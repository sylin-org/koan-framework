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

    /// <summary>Written by <see cref="CardsController"/>: a value a body may name that names nothing.</summary>
    public CardSuit Suit { get; set; }
}

/// <summary>What a card is of.</summary>
public enum CardSuit
{
    Hearts,
    Spades,
}

/// <summary>What a create takes beside the card.</summary>
public sealed record CardPlacing(string? Place = null, int? Copies = null, CardSuit? Lead = null);

/// <summary>Serves a card by its id or its slug, creates one with the title it declares and the place it takes, changes only its title, and
/// lets only someone signed in remove one.</summary>
[Route("api/cards")]
[Declared]
public sealed class CardsController : EntityController<Card>, IModelHook<Card>
{
    [HttpGet("by/{card}")]
    public override Task<IActionResult> GetById(string card, CancellationToken ct) => base.GetById(card, ct);

    [Writes(nameof(Card.Id), nameof(Card.Title), nameof(Card.Suit))]
    [Input<CardPlacing>]
    public override Task<IActionResult> Upsert(Card model, CancellationToken ct) => base.Upsert(model, ct);

    [HttpPatch("by/{card}")]
    [Writes(nameof(Card.Title), nameof(Card.Suit))]
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

/// <summary>WEB-0089: serves all six operations of a card, each of its hooks writing the operation it is told into <see cref="Told"/>.</summary>
[Route("api/told-cards")]
[Declared]
public sealed class ToldCardsController : EntityController<Card>, IRequestOptionsHook<Card>, IModelHook<Card>, IEmitHook<Card>
{
    /// <summary>The header each hook adds its step and its operation to.</summary>
    public const string Told = "Told";

    [HttpGet("")]
    [Pagination(DefaultSize = 1, MaxSize = 1, DefaultSort = nameof(Card.Title))]
    public override Task<IActionResult> GetCollection(CancellationToken ct) => base.GetCollection(ct);

    [HttpGet("new")]
    public override Task<IActionResult> GetNew(CancellationToken ct) => base.GetNew(ct);

    [HttpGet("{card}")]
    public override Task<IActionResult> GetById(string card, CancellationToken ct) => base.GetById(card, ct);

    [Writes(nameof(Card.Id), nameof(Card.Title))]
    public override Task<IActionResult> Upsert(Card model, CancellationToken ct) => base.Upsert(model, ct);

    [HttpPatch("{card}")]
    [Writes(nameof(Card.Title))]
    public override Task<IActionResult> Patch(string card, CancellationToken ct) => base.Patch(card, ct);

    [HttpDelete("{card}")]
    public override Task<IActionResult> Delete(string card, CancellationToken ct) => base.Delete(card, ct);

    public Task OnBuildingOptions(HookContext<Card> ctx, QueryOptions opts) => Tell(ctx, "options");

    public Task OnBeforeSave(HookContext<Card> ctx, Card model) => Tell(ctx, "save");

    public Task OnBeforeDelete(HookContext<Card> ctx, Card model) => Tell(ctx, "delete");

    public async Task<EmitDecision> OnEmitCollection(HookContext<Card> ctx, object payload)
    {
        await Tell(ctx, "emit");
        return EmitDecision.Next();
    }

    public async Task<EmitDecision> OnEmitModel(HookContext<Card> ctx, object payload)
    {
        await Tell(ctx, "emit");
        return EmitDecision.Next();
    }

    private static Task Tell(HookContext<Card> ctx, string step)
    {
        ctx.ResponseHeaders[Told] = (ctx.ResponseHeaders.TryGetValue(Told, out var told) ? told + " " : "") + step + ":" + ctx.Operation;
        return Task.CompletedTask;
    }
}

/// <summary>WEB-0089: a create whose refused form reads the page its action hands through <c>Kept</c>, and reads the refusal where the
/// action composes none.</summary>
[Route("api/paged-cards")]
[Declared]
public sealed class PagedCardsController : EntityController<Card>, IModelHook<Card>
{
    /// <summary>A title a create refuses in its hold, whose form reads its page again.</summary>
    public const string Held = "Held card";

    /// <summary>A title a create refuses in its hold, for which the action composes no page.</summary>
    public const string Unpaged = "Unpaged card";

    [Writes(nameof(Card.Id), nameof(Card.Title))]
    public override async Task<IActionResult> Upsert(Card model, CancellationToken ct)
        => await this.Kept(await base.Upsert(model, ct),
            refused => Task.FromResult<object?>(model.Title == Unpaged ? null : new CardFormPage(refused.Word, refused.Sent)));

    public Task OnBeforeSave(HookContext<Card> ctx, Card model)
        => model.Title is Held or Unpaged
            ? throw new Koan.Data.Core.Commands.Refusal(Koan.Data.Core.Commands.Reason.Conflict("held", "This title is held."))
            : Task.CompletedTask;
}

/// <summary>The page <see cref="PagedCardsController"/> hands a refused form: why it was refused, and what it sent, for the form to be
/// shown again as it was filled.</summary>
public sealed record CardFormPage(string Word, IReadOnlyDictionary<string, string[]>? Sent);

/// <summary>The whole surface over the same row, with no hook of its own.</summary>
[Route("api/plain-cards")]
public sealed class PlainCardsController : EntityController<Card>;
