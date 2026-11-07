using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Web.Attributes;
using Koan.Web.Authorization;
using Koan.Web.Controllers;
using Koan.Web.Hooks;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Web.AdapterSurface.TestKit.Declared;

/// <summary>WEB-0087: a row three controllers serve, one declared, one a declared singleton, one the whole surface.</summary>
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

/// <summary>The whole surface over the same row, with no hook of its own.</summary>
[Route("api/plain-cards")]
public sealed class PlainCardsController : EntityController<Card>;
