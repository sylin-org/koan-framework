using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Core.Pipeline;
using Koan.Testing.Containers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// DATA-0106 §2/§4 — the read-filter contributor seam proven predicate-generic on the real SQLite adapter
/// (RowScoped, real <c>AddKoan()</c>, ARCH-0079). The decisive <b>"would Moderation hit a wall?"</b> test: a fake
/// moderation axis rides as <i>pure contributors</i> — a flattened managed field <c>__mod_status</c> stamped on write
/// (<see cref="ManagedFieldDescriptor.AutoReadFilter"/> = <c>false</c>, so the built-in equality contributor adds NO
/// auto-equality), plus an <see cref="IReadFilterContributor"/> that scopes reads by a <b>non-equality</b> predicate
/// (<c>__mod_status != "hidden"</c>). It must AND-fold into every read site — Query/All, Count, the get-by-id and
/// delete-by-id IDOR lowering, and the mass-delete paths (DeleteMany/DeleteAll/RemoveAll) — and fail closed when the
/// predicate is not pushable (§4b). No Moderation module exists; the fake axis is the whole proof.
/// </summary>
public sealed class ReadFilterContributorSpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output), IDisposable
{
    private static readonly AsyncLocal<string?> _writeStatus = new();   // stamped on write (the row's own status)
    private static readonly AsyncLocal<bool> _moderator = new();        // a moderator sees every row (no read predicate)

    public void Dispose()
    {
        _writeStatus.Value = null;
        _moderator.Value = false;
        ManagedFieldRegistry.Reset();
    }

    private static IDisposable Writing(string status)
    {
        var prev = _writeStatus.Value;
        _writeStatus.Value = status;
        return new Pop(() => _writeStatus.Value = prev);
    }

    private static IDisposable AsModerator()
    {
        var prev = _moderator.Value;
        _moderator.Value = true;
        return new Pop(() => _moderator.Value = prev);
    }

    private sealed class Pop(Action undo) : IDisposable { public void Dispose() => undo(); }

    public sealed class Doc : Entity<Doc> { public string Title { get; set; } = ""; }

    // The moderation visibility axis as a flattened managed field. AutoReadFilter:false ⇒ it stamps + serializes + fails
    // closed, but the built-in equality contributor must NOT derive an equality on it (which would wrongly conjoin).
    private static void RegisterAxis() => ManagedFieldRegistry.Register(new ManagedFieldDescriptor(
        StorageName: "__mod_status",
        ClrType: typeof(string),
        ValueProvider: () => _writeStatus.Value,
        AppliesTo: t => t == typeof(Doc),
        RequiredCapability: DataCaps.Isolation.RowScoped,
        AutoReadFilter: false));

    // A NON-equality predicate (Filter has no Ne factory; build it from the operator).
    private static Filter Ne(string field, object? value)
        => Filter.On(FieldPath.Of(field), FilterOperator.Ne, FilterValue.Of(value));

    // The non-equality read-visibility contributor: everyone but a moderator is scoped to non-hidden rows.
    private sealed class ModerationReadContributor : IReadFilterContributor
    {
        public Filter? ReadFilter(Type entityType)
        {
            if (entityType != typeof(Doc)) return null;
            if (_moderator.Value) return null;                          // moderator sees all
            return Ne("__mod_status", "hidden");                        // a NON-equality row-visibility predicate
        }
        public Capability? RequiredCapability => DataCaps.Isolation.RowScoped;
        public bool ExcludesFromCache(Type entityType) => entityType == typeof(Doc);
    }

    // A contributor whose predicate the adapter cannot push (IgnoreCase is NOT in RelationalFilterSupport) — §4b.
    private sealed class UnpushableReadContributor : IReadFilterContributor
    {
        public Filter? ReadFilter(Type entityType)
            => entityType == typeof(Doc)
                ? new FieldFilter(FieldPath.Of("Title"), FilterOperator.Eq, FilterValue.Of("x"), IgnoreCase: true)
                : null;
        public Capability? RequiredCapability => DataCaps.Isolation.RowScoped;
        public bool ExcludesFromCache(Type entityType) => entityType == typeof(Doc);
    }

    // A PURE predicate axis — a read-filter contributor with NO managed field, scoping on a CLR property. The
    // moderation shape DATA-0106 §2 blesses. Proves the raw/CAS fail-closed rides the contributor union (not _managed).
    private sealed class TitleReadContributor : IReadFilterContributor
    {
        public Filter? ReadFilter(Type entityType)
            => entityType == typeof(Doc) ? Ne("Title", "secret") : null;
        public Capability? RequiredCapability => DataCaps.Isolation.RowScoped;
        public bool ExcludesFromCache(Type entityType) => entityType == typeof(Doc);
    }

    [Fact(DisplayName = "Sqlite: a non-equality read-filter contributor scopes Query/Count and adds no auto-equality for the moderator")]
    public async Task Non_equality_predicate_scopes_query_and_count()
    {
        RequireBackingStore();
        RegisterAxis();
        await using var host = await BootAsync(s => s.AddSingleton<IReadFilterContributor, ModerationReadContributor>());
        using var _ = Lease(NewPartition());

        Doc visible, hidden;
        using (Writing("visible")) visible = await new Doc { Title = "v" }.Save();
        using (Writing("hidden")) hidden = await new Doc { Title = "h" }.Save();

        // Query/All — a normal viewer never sees the hidden row; the predicate folds at the store.
        (await Doc.All()).Select(d => d.Id).Should().Equal(visible.Id);
        // Moderator sees BOTH — proving AutoReadFilter=false added no auto-equality on __mod_status (else the
        // moderator, whose own predicate is null, would still be equality-filtered to one status and miss a row).
        using (AsModerator()) (await Doc.All()).Select(d => d.Id).Should().BeEquivalentTo(new[] { visible.Id, hidden.Id });

        // Count folds the same predicate (a distinct read site).
        (await Data<Doc, string>.Count(QueryDefinition.All)).Should().Be(1);
        using (AsModerator()) (await Data<Doc, string>.Count(QueryDefinition.All)).Should().Be(2);
    }

    [Fact(DisplayName = "Sqlite: a non-equality read-filter contributor IDOR-protects get-by-id and delete-by-id")]
    public async Task Non_equality_predicate_protects_key_operations()
    {
        RequireBackingStore();
        RegisterAxis();
        await using var host = await BootAsync(s => s.AddSingleton<IReadFilterContributor, ModerationReadContributor>());
        using var _ = Lease(NewPartition());

        Doc visible, hidden;
        using (Writing("visible")) visible = await new Doc { Title = "v" }.Save();
        using (Writing("hidden")) hidden = await new Doc { Title = "h" }.Save();

        // get-by-id IDOR — a viewer cannot fetch the hidden row by guessing its id; GetMany returns only the visible.
        (await Doc.Get(hidden.Id)).Should().BeNull();
        (await Doc.Get(visible.Id)).Should().NotBeNull();
        (await Doc.Get(new[] { visible.Id, hidden.Id })).Where(d => d is not null).Select(d => d!.Id).Should().Equal(visible.Id);
        using (AsModerator()) (await Doc.Get(hidden.Id)).Should().NotBeNull();

        // delete-by-id IDOR — a viewer cannot delete the hidden row it cannot see.
        (await Doc.Remove(hidden.Id)).Should().BeFalse();
        using (AsModerator()) (await Doc.Get(hidden.Id)).Should().NotBeNull();   // still present
    }

    [Fact(DisplayName = "Sqlite: a non-equality read-filter contributor scopes the mass-delete paths (DeleteMany/DeleteAll/RemoveAll)")]
    public async Task Non_equality_predicate_scopes_mass_deletes()
    {
        RequireBackingStore();
        RegisterAxis();
        await using var host = await BootAsync(s => s.AddSingleton<IReadFilterContributor, ModerationReadContributor>());
        using var _ = Lease(NewPartition());

        Doc visible, hidden;
        using (Writing("visible")) visible = await new Doc { Title = "v" }.Save();
        using (Writing("hidden")) hidden = await new Doc { Title = "h" }.Save();

        // DeleteMany over both ids only deletes the visible one (the hidden is not in the viewer's scope).
        (await Doc.Remove(new[] { visible.Id, hidden.Id })).Should().Be(1);
        using (AsModerator()) (await Doc.Get(hidden.Id)).Should().NotBeNull();

        // Re-add a visible row, then DeleteAll under the viewer scope wipes only the viewer-visible rows; hidden survives.
        Doc visible2;
        using (Writing("visible")) visible2 = await new Doc { Title = "v2" }.Save();
        await Data<Doc, string>.DeleteAll();
        using (AsModerator()) (await Doc.All()).Select(d => d.Id).Should().Equal(hidden.Id);

        // RemoveAll is likewise a SCOPED wipe — never an unscoped truncate across the visibility boundary.
        using (Writing("visible")) await new Doc { Title = "v3" }.Save();
        await Doc.RemoveAll();
        using (AsModerator()) (await Doc.All()).Select(d => d.Id).Should().Equal(hidden.Id);
    }

    [Fact(DisplayName = "Sqlite: a read-filter contributor whose predicate the adapter cannot push fails closed (no in-memory residual leak)")]
    public async Task Unpushable_read_predicate_fails_closed()
    {
        RequireBackingStore();
        RegisterAxis();
        await using var host = await BootAsync(s => s.AddSingleton<IReadFilterContributor, UnpushableReadContributor>());
        using var _ = Lease(NewPartition());

        // IgnoreCase is not in RelationalFilterSupport → the isolation predicate would residual-filter in memory (a leak).
        // §4b: the read fails closed instead.
        var act = async () => await Doc.All();
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cannot fully*");
    }

    [Fact(DisplayName = "Sqlite: a PURE predicate read-filter axis (no managed field) fails closed on raw query")]
    public async Task Pure_predicate_axis_fails_closed_on_raw_query()
    {
        RequireBackingStore();
        // NOTE: no RegisterAxis() — this is the pure-predicate (moderation) shape with NO managed field. The old guard
        // gated on _managed.Count>0 and would have let the opaque raw SQL slip through UNISOLATED (adversarial-review
        // HIGH #1). The fail-closed trigger now rides the contributor union via IsReadScoped().
        await using var host = await BootAsync(s => s.AddSingleton<IReadFilterContributor, TitleReadContributor>());
        using var _ = Lease(NewPartition());

        var act = async () => await Doc.QueryRaw("SELECT Id, Json FROM Doc");
        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
