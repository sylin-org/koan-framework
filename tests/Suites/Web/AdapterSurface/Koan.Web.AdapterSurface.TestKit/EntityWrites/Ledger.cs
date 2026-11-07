using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Model;
using Koan.Web.Authorization;
using Koan.Web.Controllers;
using Koan.Web.Hooks;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Web.AdapterSurface.TestKit.EntityWrites;

/// <summary>WEB-0086: a row whose write a hook extends with a line of its own, so a spec can see whether both stand or neither.</summary>
[StorageName("ledgers_surface")]
public sealed class Ledger : Entity<Ledger>
{
    public string Note { get; set; } = "";

    /// <summary>Stamped on create by <see cref="LedgerAccess"/>, which makes a create an insert; it also says who may read and change it.</summary>
    public string Desk { get; set; } = "";
}

/// <summary>What the ledger hook writes beside each ledger it saves.</summary>
[StorageName("ledger_lines_surface")]
public sealed class LedgerLine : Entity<LedgerLine>
{
    public string LedgerId { get; set; } = "";
}

/// <summary>What a written ledger answers, composed by the emit hook.</summary>
public sealed record LedgerReceipt(string Id, string Note, bool Emitted);

[Route("api/ledgers")]
public sealed class LedgerController : EntityController<Ledger>;

/// <summary>The notes that make the ledger hook do something other than write its line.</summary>
public static class LedgerNotes
{
    public const string RefuseBefore = "refuse-before";
    public const string RefuseAfter = "refuse-after";
    public const string StopAfter = "stop-after";
    public const string Cancel = "cancel";
    public const string Replay = "replay";
    public const string Race = "race";
    public const string HiddenRace = "race-hidden";
    public const string CancelCode = "ledger.cancelled";
    public const string AfterWord = "ledger-closed";
}

/// <summary>Declares the ledger's one lifecycle rule, which cancels a ledger noted <see cref="LedgerNotes.Cancel"/>.</summary>
public static class LedgerRules
{
    public static void Declare() => Ledger.Lifecycle.BeforeUpsert(context => context.Current.Note == LedgerNotes.Cancel
        ? context.Cancel("This ledger cannot be kept.", LedgerNotes.CancelCode)
        : context.Proceed());
}

/// <summary>
/// Stamps the front desk on create, so a create is an insert and an id another write took first is a conflict. The caller changes only
/// the front desk's ledgers and reads every ledger but the back desk's.
/// </summary>
public sealed class LedgerAccess : EntityAccess<Ledger>
{
    public const string Front = "front";
    public const string Back = "back";

    public override IAccessFilter<Ledger> Constrain(IAccessFilter<Ledger> filter, AccessAction action) => action switch
    {
        AccessAction.Create => filter.Stamp(ledger => ledger.Desk, Front),
        AccessAction.Update => filter.Where(ledger => ledger.Desk == Front),
        AccessAction.Read => filter.Where(ledger => ledger.Desk != Back),
        _ => filter
    };
}

/// <summary>Writes a line beside every ledger before it is saved, and refuses, stops or replays as the ledger's note says.</summary>
public sealed class LedgerHook : IModelHook<Ledger>, IEmitHook<Ledger>
{
    public int Order => 0;

    public Task OnBeforeFetch(HookContext<Ledger> ctx, string id) => Task.CompletedTask;
    public Task OnAfterFetch(HookContext<Ledger> ctx, Ledger? model) => Task.CompletedTask;
    public Task OnBeforePatch(HookContext<Ledger> ctx, string id, object patch) => Task.CompletedTask;
    public Task OnAfterPatch(HookContext<Ledger> ctx, Ledger model) => Task.CompletedTask;
    public Task OnBeforeDelete(HookContext<Ledger> ctx, Ledger model) => Task.CompletedTask;
    public Task OnAfterDelete(HookContext<Ledger> ctx, Ledger model) => Task.CompletedTask;

    public async Task OnBeforeSave(HookContext<Ledger> ctx, Ledger model)
    {
        if (model.Note == LedgerNotes.Replay && ctx.Prior is { } prior)
        {
            ctx.ShortCircuit(prior);
            return;
        }

        await new LedgerLine { LedgerId = model.Id }.Save(ctx.Ct);
        if (model.Note == LedgerNotes.RefuseBefore) throw new Refusal(Reason.Invalid("note", "A ledger cannot say that."));
        if (model.Note == LedgerNotes.Race) await new Ledger { Id = model.Id, Note = "first" }.Save(ctx.Ct);
        if (model.Note == LedgerNotes.HiddenRace) await new Ledger { Id = model.Id, Note = "first", Desk = LedgerAccess.Back }.Save(ctx.Ct);
    }

    public Task OnAfterSave(HookContext<Ledger> ctx, Ledger model)
    {
        if (model.Note == LedgerNotes.RefuseAfter) throw new Refusal(Reason.Conflict(LedgerNotes.AfterWord, "This ledger is closed."));
        if (model.Note == LedgerNotes.StopAfter) ctx.ShortCircuit(new ConflictObjectResult(new { error = "stopped" }));
        return Task.CompletedTask;
    }

    public Task<EmitDecision> OnEmitCollection(HookContext<Ledger> ctx, object payload) => Task.FromResult<EmitDecision>(EmitDecision.Next());

    public Task<EmitDecision> OnEmitModel(HookContext<Ledger> ctx, object payload)
        => Task.FromResult<EmitDecision>(payload is Ledger ledger ? EmitDecision.With(new LedgerReceipt(ledger.Id, ledger.Note, true)) : EmitDecision.Next());
}
