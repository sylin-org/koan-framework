using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Identity;
using Koan.Identity.Roles;
using Koan.Testing.Containers;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Identity.Mongo.Tests;

public sealed class ScopedRoleMongoSpec(MongoFixture fixture)
{
    [Fact]
    public async Task Mongo_enforces_scoped_queries_membership_collections_and_lifecycle_authority_freshness()
    {
        var signals = new AuthoritySignals();
        await using var host = await KoanIntegrationHost.Configure()
            .WithSettings(fixture.SettingsForBoot())
            .ConfigureServices(services =>
            {
                services.AddSingleton<IScopedRoleCatalogContributor, MongoCatalog>();
                services.AddScoped<IScopedRoleAuthorityContributor, MongoAuthority>();
                services.AddSingleton(signals);
                services.AddSingleton<IIdentityActorAccessor>(new FixedActor("owner:mongo"));
                services.AddSingleton<IScopedRoleSubjectAccessor>(new FixedSubject("participant:mongo"));
                services.AddKoan();
            })
            .StartAsync(TestContext.Current.CancellationToken);
        using var app = AppHost.PushScope(host.Services);
        using var serviceScope = host.Services.CreateScope();
        var engine = serviceScope.ServiceProvider.GetRequiredService<RoleEngine>();
        var authority = await new MongoAuthorityState
        {
            Id = MongoAuthority.AuthorityId,
            Enabled = true,
            Version = 1,
        }.Save();
        var root = new ScopedRoleScopeRef("tenant:mongo", "space", "space:mongo");
        var topic = new ScopedRoleScopeRef(root.TenantId, "topic", "topic:mongo");

        await engine.Register(new(root));
        await engine.Register(new(topic, root));
        var reader = await engine.Define(new(root, "Reader", [new("discussion.read")]));
        await engine.Define(new(topic, "Local one", []));
        await engine.Define(new(topic, "Local two", []));
        var membership = new ScopedRoleMember(root, "participant:mongo", reader.Id);
        (await engine.Add(membership)).Should().BeTrue();
        (await engine.Add(membership)).Should().BeFalse();
        await engine.Add(new(root, "participant:mongo:second", reader.Id));
        var participant = await engine.Memberships(membership.Subject, root);
        participant.Roles.Should().ContainSingle().Which.Should().Be(reader.Id);
        participant.Groups.Should().BeEmpty();
        var memberPage = await engine.Members(reader.Id, root, page: 1, pageSize: 1);
        memberPage.Items.Should().ContainSingle();
        memberPage.TotalCount.Should().Be(2);

        await new MongoPost { TenantId = root.TenantId, TopicId = topic.Id, Body = "one" }.Save();
        await new MongoPost { TenantId = root.TenantId, TopicId = topic.Id, Body = "two" }.Save();
        var hidden = await new MongoPost { TenantId = root.TenantId, TopicId = "topic:other", Body = "hidden" }.Save();
        var foreign = await new MongoPost { TenantId = "tenant:foreign", TopicId = topic.Id, Body = "foreign tenant" }.Save();

        var requested = new QueryDefinition { Page = 1, PageSize = 1, CountStrategy = CountStrategy.Exact };
        var result = await engine.QueryWithCount<MongoPost>(ScopedRoleResourceActions.Read, topic, requested);
        result.Items.Should().ContainSingle().Which.TopicId.Should().Be(topic.Id);
        result.TotalCount.Should().Be(2);
        (await engine.Count<MongoPost>(ScopedRoleResourceActions.Read, topic)).Should().Be(2);
        (await engine.Get<MongoPost>(hidden.Id, ScopedRoleResourceActions.Read, topic)).Should().BeNull();
        (await engine.Get<MongoPost>(foreign.Id, ScopedRoleResourceActions.Read, topic)).Should().BeNull();
        var roleDirectory = await engine.Roles(topic, page: 1, pageSize: 1);
        roleDirectory.Items.Should().ContainSingle();
        roleDirectory.TotalCount.Should().Be(2);

        var capabilities = Data<MongoPost, string>.Capabilities;
        capabilities.Has(DataCaps.Query.ProviderBoundedPaging).Should().BeTrue();
        capabilities.Has(DataCaps.Write.ConditionalReplace).Should().BeTrue();
        capabilities.Has(DataCaps.Write.ConditionalDelete).Should().BeTrue();
        capabilities.Has(DataCaps.Write.InsertOnly).Should().BeTrue();

        (await engine.Remove(membership)).Should().BeTrue();
        (await engine.Remove(membership)).Should().BeFalse();
        (await ScopedRoleParticipant.Get(ScopedRoleParticipant.KeyFor(root, membership.Subject),
            TestContext.Current.CancellationToken)).Should().BeNull(
                "Mongo physically deletes an empty participant collection");
        await FluentActions.Awaiting(() => engine.Query<MongoPost>(ScopedRoleResourceActions.Read, topic))
            .Should().ThrowAsync<ScopedRoleAuthorizationException>();

        signals.PauseNextValidation();
        var pending = engine.Edit(new(reader.Id, reader.Version, Name: "must not commit"));
        await signals.ValidationEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        try
        {
            var disabled = new MongoAuthorityState
            {
                Id = authority.Id,
                Enabled = false,
                Version = 2,
            };
            (await disabled.ReplaceIf(row => row.Version == authority.Version,
                ct: TestContext.Current.CancellationToken)).Should().BeTrue();
        }
        finally
        {
            signals.ReleaseValidation.TrySetResult();
        }

        await FluentActions.Awaiting(() => pending).Should().ThrowAsync<ScopedRoleAuthorizationException>()
            .Where(error => error.Code == "authority.stale");
        var stored = await ScopedRoleDefinition.Get(reader.Id, TestContext.Current.CancellationToken);
        stored.Should().NotBeNull();
        stored!.Name.Should().Be(reader.Name);
        stored.Version.Should().Be(reader.Version);
    }

    private sealed class MongoCatalog : IScopedRoleCatalogContributor
    {
        public void Describe(ScopedRoleCatalogBuilder catalog)
        {
            catalog.Scope("space");
            catalog.Scope("topic", "space");
            catalog.Capability("discussion.read", ["space", "topic"]);
            catalog.Resource<MongoPost>("topic", post => post.TenantId, post => post.TopicId).Read("discussion.read");
        }
    }

    private sealed class MongoAuthority(AuthoritySignals signals) : IScopedRoleAuthorityContributor
    {
        internal const string AuthorityId = "owner:mongo:authority";
        private static readonly IReadOnlySet<ScopedRoleAuthorityOperation> Operations =
            Enum.GetValues<ScopedRoleAuthorityOperation>().ToHashSet();

        public async ValueTask<IReadOnlyList<ScopedRoleAuthorityEnvelope>> Contribute(
            ScopedRoleAuthorityRequest request, CancellationToken ct = default)
        {
            var current = await MongoAuthorityState.Get(AuthorityId, ct).ConfigureAwait(false);
            return current is { Enabled: true }
                ? [new(request.Target, Operations, Descendants: true,
                    ProofKey: current.Id, ProofVersion: current.Version)]
                : [];
        }

        public async ValueTask<bool> Validate(ScopedRoleAuthorityRequest request,
            ScopedRoleAuthorityEnvelope envelope, CancellationToken ct = default)
        {
            if (signals.ConsumePause())
            {
                signals.ValidationEntered.TrySetResult();
                await signals.ReleaseValidation.Task.WaitAsync(ct).ConfigureAwait(false);
            }
            var current = await MongoAuthorityState.Get(envelope.ProofKey!, ct).ConfigureAwait(false);
            return current is { Enabled: true } && current.Version == envelope.ProofVersion;
        }
    }

    private sealed class AuthoritySignals
    {
        private int _pause;
        public TaskCompletionSource ValidationEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseValidation { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void PauseNextValidation() => Interlocked.Exchange(ref _pause, 1);
        public bool ConsumePause() => Interlocked.Exchange(ref _pause, 0) == 1;
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

public sealed class MongoAuthorityState : Entity<MongoAuthorityState>
{
    public bool Enabled { get; set; }
    public long Version { get; set; }
}

public sealed class MongoPost : Entity<MongoPost>
{
    public string TenantId { get; set; } = "";
    public string TopicId { get; set; } = "";
    public string Body { get; set; } = "";
}
