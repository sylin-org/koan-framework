using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Identity;
using Koan.Identity.Roles;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Identity.Sqlite.Tests;

public sealed class ScopedRoleSqliteSpec
{
    [Fact]
    public async Task Sqlite_pushes_scope_filter_pagination_count_and_CAS_through_one_plan()
    {
        var path = Path.Combine(Path.GetTempPath(), $"koan-scoped-role-{Guid.CreateVersion7():n}.db");
        try
        {
            await using var host = await KoanIntegrationHost.Configure()
                .WithSettings(new Dictionary<string, string?>
                {
                    ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
                    ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={path};Pooling=False",
                })
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IScopedRoleCatalogContributor, SqliteCatalog>();
                    services.AddScoped<IScopedRoleAuthorityContributor, SqliteAuthority>();
                    services.AddSingleton<IIdentityActorAccessor>(new FixedActor("owner:sqlite"));
                    services.AddSingleton<IScopedRoleSubjectAccessor>(new FixedSubject("participant:sqlite"));
                    services.AddKoan();
                })
                .StartAsync();
            using var app = AppHost.PushScope(host.Services);
            using var serviceScope = host.Services.CreateScope();
            var engine = serviceScope.ServiceProvider.GetRequiredService<RoleEngine>();
            var root = new ScopedRoleScopeRef("tenant:sqlite", "space", "space:sqlite");
            var topic = new ScopedRoleScopeRef(root.TenantId, "topic", "topic:sqlite");
            await engine.Register(new(root));
            await engine.Register(new(topic, root));
            var reader = await engine.Define(new(root, "Reader", [new("discussion.read")]));
            await engine.Assign(new(root, "participant:sqlite", reader.Id, ScopedRolePropagation.Descendants));

            await new SqlitePost { TenantId = root.TenantId, TopicId = topic.Id, Body = "one" }.Save();
            await new SqlitePost { TenantId = root.TenantId, TopicId = topic.Id, Body = "two" }.Save();
            await new SqlitePost { TenantId = root.TenantId, TopicId = "topic:other", Body = "hidden" }.Save();
            await new SqlitePost { TenantId = "tenant:foreign", TopicId = topic.Id, Body = "foreign tenant" }.Save();

            var query = new QueryDefinition { Page = 1, PageSize = 1, CountStrategy = CountStrategy.Exact };
            var result = await engine.QueryWithCount<SqlitePost>(ScopedRoleResourceActions.Read, topic, query);
            result.Items.Should().ContainSingle().Which.TopicId.Should().Be(topic.Id);
            result.TotalCount.Should().Be(2);
            (await engine.Count<SqlitePost>(ScopedRoleResourceActions.Read, topic)).Should().Be(2);

            var capabilities = Data<SqlitePost, string>.Capabilities;
            capabilities.Has(DataCaps.Query.ProviderBoundedPaging).Should().BeTrue();
            capabilities.Has(DataCaps.Write.ConditionalReplace).Should().BeTrue();
            capabilities.Has(DataCaps.Write.InsertOnly).Should().BeTrue();

            var staleVersion = reader.Version;
            reader = await engine.Edit(new(reader.Id, staleVersion, Name: "Reader renamed"));
            var stale = async () => await engine.Edit(new EditScopedRole(reader.Id, staleVersion, Name: "lost"));
            await stale.Should().ThrowAsync<ScopedRoleConcurrencyException>();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private sealed class SqliteCatalog : IScopedRoleCatalogContributor
    {
        public void Describe(ScopedRoleCatalogBuilder catalog)
        {
            catalog.Scope("space");
            catalog.Scope("topic", "space");
            catalog.Capability("discussion.read", ["space", "topic"]);
            catalog.Resource<SqlitePost>("topic", post => post.TenantId, post => post.TopicId).Read("discussion.read");
        }
    }

    private sealed class SqliteAuthority : IScopedRoleAuthorityContributor
    {
        private static readonly IReadOnlySet<ScopedRoleAuthorityOperation> Operations =
            Enum.GetValues<ScopedRoleAuthorityOperation>().ToHashSet();

        public ValueTask<IReadOnlyList<ScopedRoleAuthorityEnvelope>> Contribute(
            ScopedRoleAuthorityRequest request, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<ScopedRoleAuthorityEnvelope>>([
                new(request.Target, Operations, Descendants: true, ProofKey: "sqlite-owner", ProofVersion: 1)
            ]);

        public ValueTask<bool> Validate(ScopedRoleAuthorityRequest request,
            ScopedRoleAuthorityEnvelope envelope, CancellationToken ct = default)
            => ValueTask.FromResult(envelope.ProofKey == "sqlite-owner" && envelope.ProofVersion == 1);
    }

    private sealed class FixedActor(string subject) : IIdentityActorAccessor
    {
        public string? CurrentActorSubject => subject;
    }

    private sealed class FixedSubject(string subject) : IScopedRoleSubjectAccessor
    {
        public string? CurrentSubject => subject;
    }
}

public sealed class SqlitePost : Entity<SqlitePost>
{
    public string TenantId { get; set; } = "";
    public string TopicId { get; set; } = "";
    public string Body { get; set; } = "";
}
