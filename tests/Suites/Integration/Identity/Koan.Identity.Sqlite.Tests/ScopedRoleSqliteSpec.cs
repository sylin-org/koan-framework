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
            var readerBinding = await engine.Assign(new(root, "participant:sqlite", reader.Id,
                ScopedRolePropagation.Descendants));

            var removalEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseRemoval = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Koan.Data.Core.Model.Entity.Role.MemberRemoving(async context =>
            {
                if (context.Subject != "participant:sqlite") return ScopedRoleChangeDecision.Continue();
                removalEntered.TrySetResult();
                await releaseRemoval.Task;
                return ScopedRoleChangeDecision.Continue();
            });
            try
            {
                var staleRemoval = engine.Revoke(readerBinding.Id, readerBinding.Version);
                await removalEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                readerBinding = await engine.Reapprove(readerBinding.Id, readerBinding.Version);
                releaseRemoval.TrySetResult();
                await FluentActions.Awaiting(() => staleRemoval).Should().ThrowAsync<ScopedRoleConcurrencyException>();
                (await ScopedRoleBinding.Get(readerBinding.Id))!.Version.Should().Be(readerBinding.Version,
                    "the SQLite conditional delete must not remove a concurrently reapproved generation");
            }
            finally
            {
                releaseRemoval.TrySetResult();
                Koan.Data.Core.Model.Entity.Role.Reset();
            }

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
            capabilities.Has(DataCaps.Write.ConditionalDelete).Should().BeTrue();
            capabilities.Has(DataCaps.Write.InsertOnly).Should().BeTrue();

            var staleVersion = reader.Version;
            reader = await engine.Edit(new(reader.Id, staleVersion, Name: "Reader renamed"));
            var stale = async () => await engine.Edit(new EditScopedRole(reader.Id, staleVersion, Name: "lost"));
            await stale.Should().ThrowAsync<ScopedRoleConcurrencyException>();

            var allowedRole = await engine.Define(new(topic, "Visible reader", [new("discussion.read")],
                Id: "role:sqlite:reader"));
            var sensitiveRole = await engine.Define(new(topic, "Hidden moderator", [new("discussion.moderate")],
                Id: "role:sqlite:moderator"));
            var allowedBinding = await engine.Assign(new(topic, "participant:visible", allowedRole.Id));
            var sensitiveBinding = await engine.Assign(new(topic, "participant:hidden", sensitiveRole.Id));
            await engine.Replace(new(topic, "discussion.read", [new(ScopedRoleAudienceKind.Authenticated)]));
            await engine.Replace(new(topic, "discussion.moderate", [new(ScopedRoleAudienceKind.Authenticated)]));

            var catalog = serviceScope.ServiceProvider.GetRequiredService<ScopedRoleCatalog>();
            var limitedAuthority = new LimitedReadAuthority
            {
                Scope = topic,
                RoleIds = new HashSet<string>([allowedRole.Id], StringComparer.Ordinal),
                Capabilities = new HashSet<string>(["discussion.read"], StringComparer.Ordinal),
            };
            var limited = new RoleEngine(catalog, [limitedAuthority], [],
                Microsoft.Extensions.Options.Options.Create(new RoleEngineOptions()),
                new FixedActor("steward:sqlite"), new FixedSubject("participant:visible"));

            var roles = await limited.Roles(topic, page: 1, pageSize: 10);
            roles.Items.Should().ContainSingle().Which.Id.Should().Be(allowedRole.Id);
            roles.TotalCount.Should().Be(1);
            (await limited.Role(allowedRole.Id, topic))!.Id.Should().Be(allowedRole.Id);
            await FluentActions.Awaiting(() => limited.Role(sensitiveRole.Id, topic))
                .Should().ThrowAsync<ScopedRoleAuthorizationException>();

            var bindings = await limited.Bindings(topic, page: 1, pageSize: 10);
            bindings.Items.Should().ContainSingle().Which.Id.Should().Be(allowedBinding.Id);
            bindings.TotalCount.Should().Be(1);
            (await limited.Binding(allowedBinding.Id, topic))!.Id.Should().Be(allowedBinding.Id);
            (await limited.Binding(sensitiveBinding.Id, topic)).Should().BeNull();

            var policies = await limited.Policies(topic, page: 1, pageSize: 10);
            policies.Items.Should().ContainSingle().Which.Capability.Should().Be("discussion.read");
            policies.TotalCount.Should().Be(1);
            (await limited.Policy("discussion.read", topic))!.Capability.Should().Be("discussion.read");
            await FluentActions.Awaiting(() => limited.Policy("discussion.moderate", topic))
                .Should().ThrowAsync<ScopedRoleAuthorizationException>();
            (await limited.Preview("participant:visible", "discussion.read", topic)).Plan.Allowed.Should().BeTrue();
            await FluentActions.Awaiting(() => limited.Preview("participant:visible", "discussion.moderate", topic))
                .Should().ThrowAsync<ScopedRoleAuthorizationException>();

            var unsupported = new RoleEngine(catalog, [new LimitedReadAuthority
            {
                Scope = topic,
                RoleIds = limitedAuthority.RoleIds,
                Capabilities = limitedAuthority.Capabilities,
                GrantClauses = [],
            }], [], Microsoft.Extensions.Options.Options.Create(new RoleEngineOptions()),
                new FixedActor("steward:sqlite"), new FixedSubject("participant:visible"));
            await FluentActions.Awaiting(() => unsupported.Roles(topic, page: 1, pageSize: 10))
                .Should().ThrowAsync<ScopedRoleAuthorizationException>()
                .Where(error => error.Code == "authority.read.ceiling.unsupported");

            var emptyThenLimited = LimitedEngine(catalog, topic,
                new(Set(), Set()),
                new(Set(allowedRole.Id), Set("discussion.read")));
            (await emptyThenLimited.Roles(topic, page: 1, pageSize: 10)).Items
                .Should().ContainSingle().Which.Id.Should().Be(allowedRole.Id);

            var unsupportedThenSupported = LimitedEngine(catalog, topic,
                new(Set(allowedRole.Id), Set("discussion.read"), []),
                new(Set(allowedRole.Id), Set("discussion.read")));
            (await unsupportedThenSupported.Roles(topic, page: 1, pageSize: 10)).Items
                .Should().ContainSingle().Which.Id.Should().Be(allowedRole.Id);
            (await unsupportedThenSupported.Preview("participant:visible", "discussion.read", topic))
                .Plan.Allowed.Should().BeTrue();

            var disjoint = LimitedEngine(catalog, topic,
                new(Set(allowedRole.Id), Set("discussion.read")),
                new(Set(sensitiveRole.Id), Set("discussion.moderate")));
            var disjointRoles = await disjoint.Roles(topic, page: 1, pageSize: 10);
            disjointRoles.Items.Select(role => role.Id).Should().BeEquivalentTo([allowedRole.Id, sensitiveRole.Id]);
            disjointRoles.TotalCount.Should().Be(2);
            var disjointBindings = await disjoint.Bindings(topic, page: 1, pageSize: 10);
            disjointBindings.Items.Select(binding => binding.Id)
                .Should().BeEquivalentTo([allowedBinding.Id, sensitiveBinding.Id]);
            disjointBindings.TotalCount.Should().Be(2);
            (await disjoint.Binding(sensitiveBinding.Id, topic))!.Id.Should().Be(sensitiveBinding.Id);
            var disjointPolicies = await disjoint.Policies(topic, page: 1, pageSize: 10);
            disjointPolicies.Items.Select(policy => policy.Capability)
                .Should().BeEquivalentTo(["discussion.read", "discussion.moderate"]);
            disjointPolicies.TotalCount.Should().Be(2);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static RoleEngine LimitedEngine(ScopedRoleCatalog catalog, ScopedRoleScopeRef scope,
        params LimitedReadEnvelope[] alternatives)
        => new(catalog, [new LimitedReadAuthority { Scope = scope, Alternatives = alternatives }], [],
            Microsoft.Extensions.Options.Options.Create(new RoleEngineOptions()),
            new FixedActor("steward:sqlite"), new FixedSubject("participant:visible"));

    private static IReadOnlySet<string> Set(params string[] values)
        => new HashSet<string>(values, StringComparer.Ordinal);

    private sealed class SqliteCatalog : IScopedRoleCatalogContributor
    {
        public void Describe(ScopedRoleCatalogBuilder catalog)
        {
            catalog.Scope("space");
            catalog.Scope("topic", "space");
            catalog.Capability("discussion.read", ["space", "topic"]);
            catalog.Capability("discussion.moderate", ["topic"]);
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

    private sealed class LimitedReadAuthority : IScopedRoleAuthorityContributor
    {
        private static readonly IReadOnlySet<ScopedRoleAuthorityOperation> Operations =
            new HashSet<ScopedRoleAuthorityOperation>
            {
                ScopedRoleAuthorityOperation.ReadDefinitions,
                ScopedRoleAuthorityOperation.ReadAssignments,
                ScopedRoleAuthorityOperation.ReadPolicies,
                ScopedRoleAuthorityOperation.Preview,
            };

        public ScopedRoleScopeRef? Scope { get; init; }
        public IReadOnlySet<string>? RoleIds { get; init; }
        public IReadOnlySet<string>? Capabilities { get; init; }
        public IReadOnlyList<ScopedRoleGrantClause>? GrantClauses { get; init; }
        public IReadOnlyList<LimitedReadEnvelope>? Alternatives { get; init; }

        public ValueTask<IReadOnlyList<ScopedRoleAuthorityEnvelope>> Contribute(
            ScopedRoleAuthorityRequest request, CancellationToken ct = default)
        {
            if (Scope is null || request.Actor.Subject != "steward:sqlite")
                return ValueTask.FromResult<IReadOnlyList<ScopedRoleAuthorityEnvelope>>([]);
            var alternatives = Alternatives ?? [new(RoleIds, Capabilities, GrantClauses)];
            return ValueTask.FromResult<IReadOnlyList<ScopedRoleAuthorityEnvelope>>(alternatives.Select(item =>
                new ScopedRoleAuthorityEnvelope(Scope, Operations, RoleIds: item.RoleIds,
                    Capabilities: item.Capabilities, ProofKey: "sqlite-limited-reader", ProofVersion: 1)
                {
                    GrantClauses = item.GrantClauses,
                }).ToArray());
        }

        public ValueTask<bool> Validate(ScopedRoleAuthorityRequest request,
            ScopedRoleAuthorityEnvelope envelope, CancellationToken ct = default)
            => ValueTask.FromResult(envelope.ProofKey == "sqlite-limited-reader" && envelope.ProofVersion == 1);
    }

    private sealed record LimitedReadEnvelope(IReadOnlySet<string>? RoleIds,
        IReadOnlySet<string>? Capabilities, IReadOnlyList<ScopedRoleGrantClause>? GrantClauses = null);

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
