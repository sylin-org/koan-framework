using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Model;
using Koan.Web.Controllers;
using Koan.Web.Hooks;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Web.AdapterSurface.Sqlite.Tests;

/// <summary>
/// WEB-0086: an entity stored on a source of its own holds there, not on the ambient default route. Its write stands with its hook's line
/// on that source, and a refusal after the save leaves neither.
/// </summary>
public sealed class SqliteArchivedEntityWriteSpecs(SqliteAdapterFactory factory) : IClassFixture<SqliteAdapterFactory>
{
    [Fact]
    public async Task A_write_to_an_entity_on_its_own_source_stands_with_its_hooks_line()
    {
        var id = NewId();
        var answer = await Post(id, "kept");

        answer.StatusCode.Should().Be(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());
        (await Stored(id)).Should().Be((true, 1));
    }

    [Fact]
    public async Task A_refusal_after_the_save_leaves_nothing_on_the_entitys_own_source()
    {
        var id = NewId();
        var answer = await Post(id, ArchivedLedgerHook.RefuseAfter);

        answer.StatusCode.Should().Be(HttpStatusCode.Conflict, await answer.Content.ReadAsStringAsync());
        (await Stored(id)).Should().Be((false, 0));
    }

    private static string NewId() => Guid.CreateVersion7().ToString("N");

    private Task<HttpResponseMessage> Post(string id, string note)
        => factory.Client.PostAsJsonAsync("/api/archived-ledgers", new { id, note }, TestContext.Current.CancellationToken);

    private async Task<(bool Ledger, int Lines)> Stored(string id)
    {
        using var scope = AppHost.PushScope(factory.Services);
        var ledger = await ArchivedLedger.Get(id);
        var lines = await ArchivedLedgerLine.Query(line => line.LedgerId == id);
        return (ledger is not null, lines.Count);
    }
}

/// <summary>A ledger stored on the archive source, which the host's default route is not.</summary>
[DataSource(SqliteAdapterFactory.Archive)]
[StorageName("archived_ledgers_surface")]
public sealed class ArchivedLedger : Entity<ArchivedLedger>
{
    public string Note { get; set; } = "";
}

/// <summary>What the archived ledger's hook writes beside it, on the same source.</summary>
[DataSource(SqliteAdapterFactory.Archive)]
[StorageName("archived_ledger_lines_surface")]
public sealed class ArchivedLedgerLine : Entity<ArchivedLedgerLine>
{
    public string LedgerId { get; set; } = "";
}

[Route("api/archived-ledgers")]
public sealed class ArchivedLedgerController : EntityController<ArchivedLedger>;

/// <summary>Writes a line beside every archived ledger, and refuses after the save when its note says so.</summary>
public sealed class ArchivedLedgerHook : IModelHook<ArchivedLedger>
{
    public const string RefuseAfter = "refuse-after";

    public int Order => 0;

    public Task OnBeforeFetch(HookContext<ArchivedLedger> ctx, string id) => Task.CompletedTask;
    public Task OnAfterFetch(HookContext<ArchivedLedger> ctx, ArchivedLedger? model) => Task.CompletedTask;
    public Task OnBeforePatch(HookContext<ArchivedLedger> ctx, string id, object patch) => Task.CompletedTask;
    public Task OnAfterPatch(HookContext<ArchivedLedger> ctx, ArchivedLedger model) => Task.CompletedTask;
    public Task OnBeforeDelete(HookContext<ArchivedLedger> ctx, ArchivedLedger model) => Task.CompletedTask;
    public Task OnAfterDelete(HookContext<ArchivedLedger> ctx, ArchivedLedger model) => Task.CompletedTask;

    public async Task OnBeforeSave(HookContext<ArchivedLedger> ctx, ArchivedLedger model)
        => await new ArchivedLedgerLine { LedgerId = model.Id }.Save(ctx.Ct);

    public Task OnAfterSave(HookContext<ArchivedLedger> ctx, ArchivedLedger model)
        => model.Note == RefuseAfter
            ? throw new Refusal(Reason.Conflict("ledger-archived", "This ledger is archived."))
            : Task.CompletedTask;
}
