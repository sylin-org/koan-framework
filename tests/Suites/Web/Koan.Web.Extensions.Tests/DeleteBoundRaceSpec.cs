using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core.Decorators;
using Koan.Data.Core.Model;
using Koan.Web.Authorization;
using Koan.Web.Extensions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Web.Extensions.Tests;

/// <summary>
/// NK-293 / DATA-0156: the delete bound is checked on the loaded row, and a row can change owner before the delete
/// runs. A decorator inside the Data facade reassigns the row to another owner right after the endpoint's bound check
/// reads it. The delete must then remove nothing and answer 404, by id and by id list.
/// </summary>
[Collection(RestEntityCollection.Name)]
public sealed class DeleteBoundRaceSpec(RestEntityWebFactory fx)
{
    [Fact]
    public async Task Delete_by_id_of_a_row_that_changed_owner_after_the_check_is_404()
    {
        const string alice = "race-del-alice", bob = "race-del-bob";
        var id = await Create(alice, "single");

        DossierRace.Arm(id, bob);
        (await Send(HttpMethod.Delete, $"/api/dossier/{id}", alice)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        DossierRace.Fired.Should().BeTrue("the owner change must land between the check and the delete");
        (await Send(HttpMethod.Get, $"/api/dossier/{id}", bob)).StatusCode.Should().Be(HttpStatusCode.OK, "bob's row survives");
    }

    [Fact]
    public async Task Delete_many_skips_a_row_that_changed_owner_after_the_check()
    {
        const string alice = "race-bulk-alice", bob = "race-bulk-bob";
        var id = await Create(alice, "bulk");

        DossierRace.Arm(id, bob);
        var res = await Send(HttpMethod.Delete, "/api/dossier/bulk", alice, $"[\"{id}\"]");
        res.IsSuccessStatusCode.Should().BeTrue();
        JObject.Parse(await res.Content.ReadAsStringAsync())["deleted"]!.Value<int>().Should().Be(0);
        DossierRace.Fired.Should().BeTrue("the owner change must land between the check and the delete");
        (await Send(HttpMethod.Get, $"/api/dossier/{id}", bob)).StatusCode.Should().Be(HttpStatusCode.OK, "bob's row survives");
    }

    private async Task<string> Create(string user, string text)
    {
        var res = await Send(HttpMethod.Post, "/api/dossier", user, $"{{\"text\":\"{text}\"}}");
        res.IsSuccessStatusCode.Should().BeTrue($"create for {user} should succeed");
        var o = JObject.Parse(await res.Content.ReadAsStringAsync());
        return (string)(o["id"] ?? o["Id"])!;
    }

    private Task<HttpResponseMessage> Send(HttpMethod method, string path, string user, string? json = null)
    {
        var req = new HttpRequestMessage(method, path);
        req.Headers.Add("X-Test-User", user);
        if (json is not null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return fx.Client.SendAsync(req);
    }
}

/// <summary>An owned entity whose delete bound is its Read constraint (SEC-0013).</summary>
[RestEntity]
[StorageName("rest_dossiers")]
public sealed class Dossier : Entity<Dossier>
{
    public string? OwnerId { get; set; }
    public string Text { get; set; } = "";
}

public sealed class DossierAccess : EntityAccess<Dossier>
{
    protected override Expression<Func<Dossier, bool>>? Owner => d => d.OwnerId == CurrentUserId;

    public override IAccessFilter<Dossier> Constrain(IAccessFilter<Dossier> q, AccessAction action) => action switch
    {
        AccessAction.Create => q.Stamp(d => d.OwnerId, CurrentUserId),
        AccessAction.Read => q.Where(Owner!),
        _ => q,
    };
}

/// <summary>Reassigns one armed row to another owner as soon as a read returns it, once.</summary>
public sealed class DossierRace : IDataRepositoryDecorator
{
    private static volatile string? _id;
    private static volatile string? _owner;
    private static volatile bool _fired;

    public static bool Fired => _fired;

    public static void Arm(string id, string owner)
    {
        _fired = false;
        _owner = owner;
        _id = id;
    }

    public object? TryDecorate(Type entityType, Type keyType, object repository, IServiceProvider services)
        => entityType == typeof(Dossier) && keyType == typeof(string)
            ? new Repository((IDataRepository<Dossier, string>)repository)
            : null;

    private static async Task AfterRead(IDataRepository<Dossier, string> inner, IEnumerable<Dossier?> rows, CancellationToken ct)
    {
        if (_id is not { } id || _owner is not { } owner || !rows.Any(row => row?.Id == id)) return;
        _id = null;
        var row = (await inner.Get(id, ct))!;
        row.OwnerId = owner;
        await inner.Upsert(row, ct);
        _fired = true;
    }

    private sealed class Repository(IDataRepository<Dossier, string> inner) :
        IDataRepository<Dossier, string>,
        IQueryRepository<Dossier, string>,
        IInsertOnlyRepository<Dossier, string>,
        IConditionalDeleteRepository<Dossier, string>,
        IDescribesCapabilities
    {
        private IQueryRepository<Dossier, string> Queries => (IQueryRepository<Dossier, string>)inner;

        public void Describe(ICapabilities capabilities) => DataCaps.Describe(inner, inner.GetType().Name).CopyInto(capabilities);

        public async Task<Dossier?> Get(string id, CancellationToken ct = default)
        {
            var row = await inner.Get(id, ct);
            await AfterRead(inner, [row], ct);
            return row;
        }

        public async Task<IReadOnlyList<Dossier?>> GetMany(IEnumerable<string> ids, CancellationToken ct = default)
        {
            var rows = await inner.GetMany(ids, ct);
            await AfterRead(inner, rows, ct);
            return rows;
        }

        public async Task<RepositoryQueryResult<Dossier>> Query(QueryDefinition query, CancellationToken ct = default)
        {
            var result = await Queries.Query(query, ct);
            await AfterRead(inner, result.Items, ct);
            return result;
        }

        public Task<CountResult> Count(QueryDefinition query, CancellationToken ct = default) => Queries.Count(query, ct);
        public Task<MutationResult<Dossier, string>> Insert(Dossier model, CancellationToken ct = default)
            => ((IInsertOnlyRepository<Dossier, string>)inner).Insert(model, ct);
        public Task<bool> ConditionalDeleteAsync(string id, Filter guard, CancellationToken ct = default)
            => ((IConditionalDeleteRepository<Dossier, string>)inner).ConditionalDeleteAsync(id, guard, ct);
        public Task<Dossier> Upsert(Dossier model, CancellationToken ct = default) => inner.Upsert(model, ct);
        public Task<int> UpsertMany(IEnumerable<Dossier> models, CancellationToken ct = default) => inner.UpsertMany(models, ct);
        public Task<bool> Delete(string id, CancellationToken ct = default) => inner.Delete(id, ct);
        public Task<int> DeleteMany(IEnumerable<string> ids, CancellationToken ct = default) => inner.DeleteMany(ids, ct);
        public Task<int> DeleteAll(CancellationToken ct = default) => inner.DeleteAll(ct);
        public Task<long> RemoveAll(RemoveStrategy strategy, CancellationToken ct = default) => inner.RemoveAll(strategy, ct);
        public IBatchSet<Dossier, string> CreateBatch() => inner.CreateBatch();
        public Task EnsureReady(CancellationToken ct = default) => inner.EnsureReady(ct);
    }
}
