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

namespace Koan.Data.Connector.Mongo.Tests.Specs;

/// <summary>
/// DATA-0106 §2 — the read-filter contributor seam proven <b>adapter-agnostic</b> on MongoDB (a different store
/// family: BSON document, the MongoFilterTranslator, non-Newtonsoft). The identical fake moderation axis used on
/// SQLite rides Mongo unchanged: a flattened managed field <c>__mod_status</c> (<see cref="ManagedFieldDescriptor.AutoReadFilter"/>
/// = <c>false</c>) injected as a BSON element + an <see cref="IReadFilterContributor"/> whose <b>non-equality</b>
/// predicate (<c>__mod_status $ne "hidden"</c>) folds — through the SAME <c>FieldPathResolver</c>, no Mongo-specific
/// read branch — into Query/Count, the get-by-id / delete-by-id IDOR lowering, and the mass-delete paths. This is the
/// "would Moderation hit a wall on a non-relational adapter?" proof: it does not. The fail-closed/pushability checks
/// (§4) are adapter-agnostic facade logic proven on SQLite; Mongo pushes <c>$ne</c>, so the happy path is identical.
/// Real <c>AddKoan()</c> boot against a Testcontainers Mongo (ARCH-0079).
/// </summary>
public sealed class MongoReadFilterContributorSpec(MongoFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<MongoFixture>(fixture, output), IDisposable
{
    private static readonly AsyncLocal<string?> _writeStatus = new();
    private static readonly AsyncLocal<bool> _moderator = new();

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

    private static void RegisterAxis() => ManagedFieldRegistry.Register(new ManagedFieldDescriptor(
        StorageName: "__mod_status",
        ClrType: typeof(string),
        ValueProvider: () => _writeStatus.Value,
        AppliesTo: t => t == typeof(Doc),
        RequiredCapability: DataCaps.Isolation.RowScoped,
        AutoReadFilter: false));

    private sealed class ModerationReadContributor : IReadFilterContributor
    {
        public Filter? ReadFilter(Type entityType)
        {
            if (entityType != typeof(Doc)) return null;
            if (_moderator.Value) return null;
            return Filter.On(FieldPath.Of("__mod_status"), FilterOperator.Ne, FilterValue.Of("hidden"));
        }
        public Capability? RequiredCapability => DataCaps.Isolation.RowScoped;
        public bool ExcludesFromCache(Type entityType) => entityType == typeof(Doc);
    }

    [Fact(DisplayName = "Mongo: a non-equality read-filter contributor scopes Query/Count and adds no auto-equality for the moderator")]
    public async Task Non_equality_predicate_scopes_query_and_count()
    {
        RequireBackingStore();
        RegisterAxis();
        await using var host = await BootAsync(s => s.AddSingleton<IReadFilterContributor, ModerationReadContributor>());
        using var _ = Lease(NewPartition());

        Doc visible, hidden;
        using (Writing("visible")) visible = await new Doc { Title = "v" }.Save();
        using (Writing("hidden")) hidden = await new Doc { Title = "h" }.Save();

        (await Doc.All()).Select(d => d.Id).Should().Equal(visible.Id);
        using (AsModerator()) (await Doc.All()).Select(d => d.Id).Should().BeEquivalentTo(new[] { visible.Id, hidden.Id });

        (await Data<Doc, string>.Count(QueryDefinition.All)).Should().Be(1);
        using (AsModerator()) (await Data<Doc, string>.Count(QueryDefinition.All)).Should().Be(2);
    }

    [Fact(DisplayName = "Mongo: a non-equality read-filter contributor IDOR-protects get-by-id and delete-by-id")]
    public async Task Non_equality_predicate_protects_key_operations()
    {
        RequireBackingStore();
        RegisterAxis();
        await using var host = await BootAsync(s => s.AddSingleton<IReadFilterContributor, ModerationReadContributor>());
        using var _ = Lease(NewPartition());

        Doc visible, hidden;
        using (Writing("visible")) visible = await new Doc { Title = "v" }.Save();
        using (Writing("hidden")) hidden = await new Doc { Title = "h" }.Save();

        (await Doc.Get(hidden.Id)).Should().BeNull();
        (await Doc.Get(visible.Id)).Should().NotBeNull();
        (await Doc.Get(new[] { visible.Id, hidden.Id })).Where(d => d is not null).Select(d => d!.Id).Should().Equal(visible.Id);
        using (AsModerator()) (await Doc.Get(hidden.Id)).Should().NotBeNull();

        (await Doc.Remove(hidden.Id)).Should().BeFalse();
        using (AsModerator()) (await Doc.Get(hidden.Id)).Should().NotBeNull();
    }

    [Fact(DisplayName = "Mongo: a non-equality read-filter contributor scopes the mass-delete paths (DeleteMany/DeleteAll/RemoveAll)")]
    public async Task Non_equality_predicate_scopes_mass_deletes()
    {
        RequireBackingStore();
        RegisterAxis();
        await using var host = await BootAsync(s => s.AddSingleton<IReadFilterContributor, ModerationReadContributor>());
        using var _ = Lease(NewPartition());

        Doc visible, hidden;
        using (Writing("visible")) visible = await new Doc { Title = "v" }.Save();
        using (Writing("hidden")) hidden = await new Doc { Title = "h" }.Save();

        (await Doc.Remove(new[] { visible.Id, hidden.Id })).Should().Be(1);
        using (AsModerator()) (await Doc.Get(hidden.Id)).Should().NotBeNull();

        using (Writing("visible")) await new Doc { Title = "v2" }.Save();
        await Data<Doc, string>.DeleteAll();
        using (AsModerator()) (await Doc.All()).Select(d => d.Id).Should().Equal(hidden.Id);

        using (Writing("visible")) await new Doc { Title = "v3" }.Save();
        await Doc.RemoveAll();
        using (AsModerator()) (await Doc.All()).Select(d => d.Id).Should().Equal(hidden.Id);
    }
}
