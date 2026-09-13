using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core.Lifecycle;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Koan.Tenancy;
using Xunit;

namespace Koan.Identity.Tests;

[Collection("identity")]
public sealed class ScopedRoleEngineSpec : IdentityHostScopedSpec
{
    private readonly IdentityHostFixture _fixture;
    public ScopedRoleEngineSpec(IdentityHostFixture fixture) : base(fixture) => _fixture = fixture;

    [Fact]
    public async Task Member_plus_reader_preserves_the_valid_member_grant()
    {
        using var scope = _fixture.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RoleEngine>();
        var (owner, root, topic) = await Tree(engine);
        var member = await engine.Define(owner, new(root, "Member", [new("discussion.reply")]));
        var reader = await engine.Define(owner, new(root, "Reader", [new("discussion.read")]));
        await engine.Assign(owner, new(root, "participant:one", member.Id, ScopedRolePropagation.Descendants));
        await engine.Assign(owner, new(root, "participant:one", reader.Id, ScopedRolePropagation.Descendants));

        (await engine.Check(new("participant:one"), "discussion.reply", topic)).Should().BeTrue();
        (await engine.Check(new("participant:one"), "discussion.reply", topic,
            new Dictionary<string, object?> { ["blocked"] = true })).Should().BeFalse(
            "mandatory application guards intersect every ordinary grant");
    }

    [Fact]
    public async Task Nearest_replacement_is_the_complete_audience_and_local_binding_cannot_bypass_it()
    {
        using var scope = _fixture.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RoleEngine>();
        var (owner, root, topic) = await Tree(engine);
        var speakers = await engine.Define(owner, new(root, "Speakers", [new("discussion.reply")]));
        var guest = await engine.Define(owner, new(root, "Guest", [new("discussion.reply")]));
        await engine.Replace(owner, new(root, "discussion.reply", [new(ScopedRoleAudienceKind.Role, speakers.Id)]));
        var guestBinding = await engine.Assign(owner, new(topic, "participant:guest", guest.Id));

        (await engine.Check(new("participant:guest"), "discussion.reply", topic)).Should().BeFalse(
            "a child-local default grant cannot defeat an inherited replacement");

        await engine.Replace(owner, new(topic, "discussion.reply", [new(ScopedRoleAudienceKind.Role, guest.Id)]));
        (await engine.Check(new("participant:guest"), "discussion.reply", topic)).Should().BeFalse(
            "a later badge-role audience cannot enlarge an older binding's approved envelope");
        await engine.Reapprove(owner, guestBinding.Id, guestBinding.Version);
        (await engine.Check(new("participant:guest"), "discussion.reply", topic)).Should().BeTrue(
            "the nearest explicit child replacement is the deliberate exception");
    }

    [Fact]
    public async Task Empty_replacement_means_nobody_and_not_inherit()
    {
        using var scope = _fixture.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RoleEngine>();
        var (owner, root, topic) = await Tree(engine);
        var member = await engine.Define(owner, new(root, "Member", [new("discussion.reply")]));
        await engine.Assign(owner, new(root, "participant:empty", member.Id, ScopedRolePropagation.Descendants));
        await engine.Replace(owner, new(topic, "discussion.reply", []));

        var plan = await engine.Plan(new("participant:empty"), "discussion.reply", topic);
        plan.Allowed.Should().BeFalse();
        plan.WinningPolicyId.Should().NotBeNull();
        plan.Reasons.Should().ContainSingle(x => x.Code == "policy.replace.denied");
    }

    [Fact]
    public async Task Conditional_grant_paths_remain_correlated()
    {
        using var scope = _fixture.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RoleEngine>();
        var (owner, root, topic) = await Tree(engine);
        var approver = await engine.Define(owner, new(root, "Approver", [
            new("discussion.approve", [new("amount", ScopedRoleConditionOperator.LessThanOrEqual, "500"), new("department", ScopedRoleConditionOperator.Equal, "A")]),
            new("discussion.approve", [new("amount", ScopedRoleConditionOperator.LessThanOrEqual, "5000"), new("department", ScopedRoleConditionOperator.Equal, "B")]),
        ]));
        await engine.Assign(owner, new(root, "participant:approver", approver.Id, ScopedRolePropagation.Descendants));

        (await engine.Check(new("participant:approver"), "discussion.approve", topic,
            new Dictionary<string, object?> { ["amount"] = 4000, ["department"] = "A" })).Should().BeFalse();
        (await engine.Check(new("participant:approver"), "discussion.approve", topic,
            new Dictionary<string, object?> { ["amount"] = 4000, ["department"] = "B" })).Should().BeTrue();
    }

    [Fact]
    public async Task Authority_change_invalidates_existing_binding_until_explicit_reapproval()
    {
        using var scope = _fixture.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RoleEngine>();
        var (owner, root, topic) = await Tree(engine);
        var role = await engine.Define(owner, new(root, "Reader", [new("discussion.read")]));
        var binding = await engine.Assign(owner, new(root, "participant:versioned", role.Id, ScopedRolePropagation.Descendants));
        role = await engine.Edit(owner, new(role.Id, role.Version, Grants: [new("discussion.read"), new("discussion.reply")]));

        (await engine.Check(new("participant:versioned"), "discussion.read", topic)).Should().BeFalse(
            "all stale approved authority stops contributing after a grant expansion");
        binding = await engine.Reapprove(owner, binding.Id, binding.Version);
        binding.ApprovedRoleVersion.Should().Be(role.AuthorityVersion);
        (await engine.Check(new("participant:versioned"), "discussion.reply", topic)).Should().BeTrue();
    }

    [Fact]
    public async Task Cosmetic_edit_preserves_authority_and_stale_writes_are_rejected()
    {
        using var scope = _fixture.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RoleEngine>();
        var (owner, root, topic) = await Tree(engine);
        var role = await engine.Define(owner, new(root, "Reader", [new("discussion.read")]));
        await engine.Assign(owner, new(root, "participant:cosmetic", role.Id, ScopedRolePropagation.Descendants));
        var originalVersion = role.Version;
        var originalAuthority = role.AuthorityVersion;

        role = await engine.Edit(owner, new(role.Id, originalVersion, Name: "Reader renamed",
            Presentation: new Dictionary<string, string> { ["color"] = "blue" }));
        role.AuthorityVersion.Should().Be(originalAuthority);
        (await engine.Check(new("participant:cosmetic"), "discussion.read", topic)).Should().BeTrue();

        var stale = async () => await engine.Edit(owner, new(role.Id, originalVersion, Name: "lost update"));
        await stale.Should().ThrowAsync<ScopedRoleConcurrencyException>();
    }

    [Fact]
    public async Task Identical_assignment_retry_converges_without_renewing_or_duplicating()
    {
        using var scope = _fixture.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RoleEngine>();
        var (owner, root, _) = await Tree(engine);
        var role = await engine.Define(owner, new(root, "Member", []));
        var expiry = DateTimeOffset.UtcNow.AddHours(2);
        var command = new AssignScopedRole(root, "participant:retry", role.Id,
            ScopedRolePropagation.Descendants, expiry);

        var first = await engine.Assign(owner, command);
        var second = await engine.Assign(owner, command);
        second.Id.Should().Be(first.Id);
        second.ExpiresAt.Should().Be(expiry);
        second.Version.Should().Be(first.Version);
        (await ScopedRoleBinding.Query(x => x.Id == first.Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task Headless_mutations_preserve_verified_actor_attribution_in_audit()
    {
        using var scope = _fixture.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RoleEngine>();
        var (owner, root, _) = await Tree(engine);
        var role = await engine.Define(owner, new(root, "Audited", []));

        (await AuditEvent.Query(x => x.Target == $"ScopedRoleDefinition/{role.Id}"))
            .Should().ContainSingle(x => x.Actor == owner.Subject && x.Action == "scopedroledefinition.created");
    }

    [Fact]
    public async Task Revocation_and_role_disable_stop_override_role_selectors()
    {
        using var scope = _fixture.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RoleEngine>();
        var (owner, root, topic) = await Tree(engine);
        var speaker = await engine.Define(owner, new(root, "Speaker", []));
        var binding = await engine.Assign(owner, new(root, "participant:speaker", speaker.Id, ScopedRolePropagation.Descendants));
        await engine.Replace(owner, new(topic, "discussion.reply", [new(ScopedRoleAudienceKind.Role, speaker.Id)]));
        binding = await engine.Reapprove(owner, binding.Id, binding.Version);
        (await engine.Check(new("participant:speaker"), "discussion.reply", topic)).Should().BeTrue();

        binding = await engine.Revoke(owner, binding.Id, binding.Version);
        (await engine.Check(new("participant:speaker"), "discussion.reply", topic)).Should().BeFalse();

        var second = await engine.Assign(owner, new(root, "participant:second", speaker.Id, ScopedRolePropagation.Descendants));
        (await engine.Check(new("participant:second"), "discussion.reply", topic)).Should().BeTrue();
        speaker = await engine.Disable(owner, speaker.Id, speaker.Version);
        (await engine.Check(new("participant:second"), "discussion.reply", topic)).Should().BeFalse();
        second.ApprovedRoleVersion.Should().BeLessThan(speaker.AuthorityVersion);
    }

    [Fact]
    public async Task Administration_is_deny_by_default_and_self_assignment_requires_an_explicit_ceiling()
    {
        using var scope = _fixture.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RoleEngine>();
        var (owner, root, _) = await Tree(engine);
        var role = await engine.Define(owner, new(root, "Member", []));

        var denied = async () => await engine.Assign(new ScopedRoleActor("participant:ordinary"),
            new(root, "participant:ordinary", role.Id));
        await denied.Should().ThrowAsync<ScopedRoleAuthorizationException>();
        await engine.Assign(owner, new(root, owner.Subject, role.Id));
    }

    [Fact]
    public async Task Finite_expiry_ceiling_rejects_a_permanent_assignment()
    {
        using var scope = _fixture.Services.CreateScope();
        var normal = scope.ServiceProvider.GetRequiredService<RoleEngine>();
        var (owner, root, _) = await Tree(normal);
        var role = await normal.Define(owner, new(root, "Temporary", []));
        var catalog = scope.ServiceProvider.GetRequiredService<ScopedRoleCatalog>();
        var bounded = new RoleEngine(catalog, [new FiniteAuthority { Maximum = DateTimeOffset.UtcNow.AddHours(1) }], [],
            Microsoft.Extensions.Options.Options.Create(new RoleEngineOptions()));

        var permanent = async () => await bounded.Assign(owner, new(root, "participant:permanent", role.Id));
        await permanent.Should().ThrowAsync<ScopedRoleAuthorizationException>();
    }

    [Fact]
    public async Task Authority_is_revalidated_inside_the_lifecycle_commit_boundary()
    {
        using var scope = _fixture.Services.CreateScope();
        var normal = scope.ServiceProvider.GetRequiredService<RoleEngine>();
        var (owner, root, _) = await Tree(normal);
        var role = await normal.Define(owner, new(root, "Commit proof", []));
        var catalog = scope.ServiceProvider.GetRequiredService<ScopedRoleCatalog>();
        var rejecting = new CommitRejectAuthority { Enabled = true };
        var guarded = new RoleEngine(catalog, [rejecting], [],
            Microsoft.Extensions.Options.Options.Create(new RoleEngineOptions()));

        var write = async () => await guarded.Assign(owner, new(root, "participant:stale-proof", role.Id));
        await write.Should().ThrowAsync<ScopedRoleAuthorizationException>()
            .Where(x => x.Code == "authority.stale");
        (await ScopedRoleBinding.Query(x => x.Subject == "participant:stale-proof")).Should().BeEmpty();
        rejecting.Validations.Should().Be(1, "the proof is checked at lifecycle dispatch, not reused from admission");
    }

    [Fact]
    public async Task Headless_query_by_id_and_count_share_the_provider_pushed_scope_plan()
    {
        using var scope = _fixture.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RoleEngine>();
        var (owner, root, topic) = await Tree(engine);
        var sibling = new ScopedRoleScopeRef(root.TenantId, "topic", Guid.NewGuid().ToString("N"));
        await engine.Register(owner, new(sibling, root));
        var reader = await engine.Define(owner, new(root, "Reader", [new("discussion.read")]));
        await engine.Assign(owner, new(root, "participant:query", reader.Id, ScopedRolePropagation.Descendants));
        using (Tenant.Use(root.TenantId))
        {
            var visible = await new ScopedDiscussionPost { TenantId = root.TenantId, TopicId = topic.Id, Body = "visible" }.Save();
            await new ScopedDiscussionPost { TenantId = root.TenantId, TopicId = topic.Id, Body = "visible two" }.Save();
            var hidden = await new ScopedDiscussionPost { TenantId = root.TenantId, TopicId = sibling.Id, Body = "hidden" }.Save();
            await new ScopedDiscussionPost { TenantId = "tenant:foreign", TopicId = topic.Id, Body = "foreign tenant" }.Save();

            using (_fixture.Actor.Use("participant:query"))
            {
                var rows = await engine.Query<ScopedDiscussionPost>(ScopedRoleResourceActions.Read, topic);
                rows.Should().HaveCount(2).And.OnlyContain(x => x.TopicId == topic.Id);
                (await engine.Get<ScopedDiscussionPost>(visible.Id, ScopedRoleResourceActions.Read, topic)).Should().NotBeNull();
                (await engine.Get<ScopedDiscussionPost>(hidden.Id, ScopedRoleResourceActions.Read, topic)).Should().BeNull();
                (await engine.Count<ScopedDiscussionPost>(ScopedRoleResourceActions.Read, topic)).Should().Be(2);

                var paged = async () => await engine.QueryWithCount<ScopedDiscussionPost>(
                    ScopedRoleResourceActions.Read, topic,
                    new QueryDefinition { Page = 1, PageSize = 1, CountStrategy = CountStrategy.Exact });
                await paged.Should().ThrowAsync<NotSupportedException>()
                    .WithMessage("*cannot prove provider-bounded paging*");
            }
        }
    }

    [Fact]
    public async Task Delegation_envelopes_preserve_correlated_grant_and_audience_condition_ceilings()
    {
        using var scope = _fixture.Services.CreateScope();
        var normal = scope.ServiceProvider.GetRequiredService<RoleEngine>();
        var (owner, _, topic) = await Tree(normal);
        var actor = new ScopedRoleActor("delegate:bounded");
        var permittedCondition = new ScopedRoleCondition("amount", ScopedRoleConditionOperator.LessThanOrEqual, "100");
        var authority = new ClauseLimitedAuthority
        {
            Grants = [new("discussion.approve", [permittedCondition])],
            Audience = [new(ScopedRoleAudienceKind.Authenticated, Conditions: [permittedCondition])],
        };
        var bounded = new RoleEngine(scope.ServiceProvider.GetRequiredService<ScopedRoleCatalog>(), [authority], [],
            Microsoft.Extensions.Options.Options.Create(new RoleEngineOptions()));

        var role = await bounded.Define(actor, new(topic, "Bounded approver",
            [new("discussion.approve", [permittedCondition])]));
        role.Grants.Should().ContainSingle();

        var widenedRole = async () => await bounded.Define(actor, new(topic, "Widened approver",
            [new("discussion.approve", [new("amount", ScopedRoleConditionOperator.LessThanOrEqual, "1000")])]));
        await widenedRole.Should().ThrowAsync<ScopedRoleAuthorizationException>()
            .Where(error => error.Code == "authority.denied");

        var policy = await bounded.Replace(actor, new(topic, "discussion.approve",
            [new(ScopedRoleAudienceKind.Authenticated, Conditions: [permittedCondition])]));
        policy.Version.Should().Be(1);

        var widenedAudience = async () => await bounded.Replace(actor, new(topic, "discussion.approve",
            [new(ScopedRoleAudienceKind.Authenticated,
                Conditions: [new("amount", ScopedRoleConditionOperator.LessThanOrEqual, "1000")])], policy.Version));
        await widenedAudience.Should().ThrowAsync<ScopedRoleAuthorizationException>()
            .Where(error => error.Code == "authority.denied");

        var badge = await normal.Define(owner, new(topic, "Badge", []));
        policy = await normal.Replace(owner, new(topic, "discussion.approve",
            [new(ScopedRoleAudienceKind.Role, badge.Id, [permittedCondition])], policy.Version));
        (await bounded.Assign(actor, new(topic, "participant:bounded", badge.Id))).RoleId.Should().Be(badge.Id);

        policy = await normal.Replace(owner, new(topic, "discussion.approve",
            [new(ScopedRoleAudienceKind.Role, badge.Id,
                [new("amount", ScopedRoleConditionOperator.LessThanOrEqual, "1000")])], policy.Version));
        var widenedIndirectGrant = async () => await bounded.Assign(actor,
            new(topic, "participant:widened", badge.Id));
        await widenedIndirectGrant.Should().ThrowAsync<ScopedRoleAuthorizationException>()
            .Where(error => error.Code == "authority.denied");
    }

    [Fact]
    public async Task Policy_reset_requires_distinct_authority_because_inheritance_can_widen_access()
    {
        using var scope = _fixture.Services.CreateScope();
        var normal = scope.ServiceProvider.GetRequiredService<RoleEngine>();
        var (owner, root, topic) = await Tree(normal);
        var member = await normal.Define(owner, new(root, "Member", [new("discussion.reply")]));
        await normal.Assign(owner, new(root, "participant:reset", member.Id,
            ScopedRolePropagation.Descendants));
        var narrowing = await normal.Replace(owner, new(topic, "discussion.reply", []));
        (await normal.Check(new("participant:reset"), "discussion.reply", topic)).Should().BeFalse();

        var catalog = scope.ServiceProvider.GetRequiredService<ScopedRoleCatalog>();
        var steward = new ScopedRoleActor("steward:reset");
        var manageOnly = new ResetPolicyAuthority
        {
            Scope = topic,
            Capability = "discussion.reply",
            Audience = [new(ScopedRoleAudienceKind.Role, member.Id)],
        };
        var limited = new RoleEngine(catalog, [manageOnly], [],
            Microsoft.Extensions.Options.Options.Create(new RoleEngineOptions()));

        var denied = async () => await limited.Inherit(steward, topic, "discussion.reply", narrowing.Version);
        await denied.Should().ThrowAsync<ScopedRoleAuthorizationException>()
            .Where(error => error.Code == "authority.denied");
        var unchanged = await ScopedRolePolicy.Get(narrowing.Id);
        unchanged.Should().NotBeNull();
        unchanged!.Mode.Should().Be(ScopedRoleOverrideMode.Replace);
        unchanged.Version.Should().Be(narrowing.Version);
        (await normal.Check(new("participant:reset"), "discussion.reply", topic)).Should().BeFalse();

        var clauseConstrainedReset = new RoleEngine(catalog, [new ResetPolicyAuthority
        {
            Scope = topic,
            Capability = "discussion.reply",
            AllowReset = true,
            Audience = [new(ScopedRoleAudienceKind.Role, member.Id)],
        }], [], Microsoft.Extensions.Options.Options.Create(new RoleEngineOptions()));
        var unsupported = async () => await clauseConstrainedReset.Inherit(steward, topic,
            "discussion.reply", narrowing.Version);
        await unsupported.Should().ThrowAsync<ScopedRoleAuthorizationException>()
            .Where(error => error.Code == "authority.reset.ceiling.unsupported");

        var reset = new RoleEngine(catalog, [new ResetPolicyAuthority
        {
            Scope = topic,
            Capability = "discussion.reply",
            AllowReset = true,
            Audience = [new(ScopedRoleAudienceKind.Role, member.Id)],
            IncludeSupportedFallback = true,
        }], [], Microsoft.Extensions.Options.Options.Create(new RoleEngineOptions()));
        var inherited = await reset.Inherit(steward, topic, "discussion.reply", narrowing.Version);
        inherited.Mode.Should().Be(ScopedRoleOverrideMode.Inherit);
        (await normal.Check(new("participant:reset"), "discussion.reply", topic)).Should().BeTrue();
    }

    [Fact]
    public async Task Resource_enrollment_without_a_tenant_field_fails_closed()
    {
        var builder = new ScopedRoleCatalogBuilder();
        builder.Scope("topic");
        builder.Capability("discussion.read", ["topic"]);
        builder.Resource<LegacyScopedPost>("topic", post => post.TopicId).Read("discussion.read");
        var engine = new RoleEngine(builder.Build(), [], [],
            Microsoft.Extensions.Options.Options.Create(new RoleEngineOptions()));

        var constrain = async () => await engine.Constrain<LegacyScopedPost>(new ScopedRoleActor("participant"),
            ScopedRoleResourceActions.Read, new("tenant", "topic", "same-id"));
        await constrain.Should().ThrowAsync<ScopedRoleValidationException>()
            .Where(error => error.Code == "resource.tenant.unenrolled");
    }

    [Fact]
    public void Additive_management_contracts_preserve_released_enum_and_constructor_shapes()
    {
        ((int)ScopedRoleAuthorityOperation.RegisterScope).Should().Be(0);
        ((int)ScopedRoleAuthorityOperation.DefineRole).Should().Be(1);
        ((int)ScopedRoleAuthorityOperation.EditRole).Should().Be(2);
        ((int)ScopedRoleAuthorityOperation.AssignRole).Should().Be(3);
        ((int)ScopedRoleAuthorityOperation.RevokeRole).Should().Be(4);
        ((int)ScopedRoleAuthorityOperation.ManagePolicy).Should().Be(5);
        ((int)ScopedRoleAuthorityOperation.Preview).Should().Be(6);
        ((int)ScopedRoleAuthorityOperation.ReadAudit).Should().Be(7);
        ((int)ScopedRoleAuthorityOperation.ResetPolicy).Should().Be(11);
        typeof(ScopedRoleAuthorityRequest).GetConstructors().Should()
            .Contain(constructor => constructor.GetParameters().Length == 9);
        typeof(ScopedRoleAuthorityEnvelope).GetConstructors().Should()
            .Contain(constructor => constructor.GetParameters().Length == 10);
    }

    [Fact]
    public async Task Public_operations_bind_the_verified_actor_instead_of_accepting_one_from_the_caller()
    {
        using var scope = _fixture.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RoleEngine>();
        var suffix = Guid.NewGuid().ToString("N");
        var root = new ScopedRoleScopeRef($"tenant:{suffix}", "space", $"space:{suffix}");

        var noActor = async () => await engine.Register(new RegisterScopedRoleScope(root));
        await noActor.Should().ThrowAsync<ScopedRoleAuthorizationException>()
            .Where(x => x.Code == "actor.unavailable");

        var http = _fixture.Services.GetRequiredService<IHttpContextAccessor>();
        var prior = http.HttpContext;
        http.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, $"owner:{suffix}")
            ], "test"))
        };
        try
        {
            using (_fixture.Actor.Use($"owner:{suffix}"))
            {
                await engine.Register(new RegisterScopedRoleScope(root));
                var role = await engine.Define(new DefineScopedRole(root, "Verified", []));
                await engine.Assign(new AssignScopedRole(root, $"owner:{suffix}", role.Id));
                (await engine.Plan("discussion.read", root)).Subject.Subject.Should().Be($"owner:{suffix}");
            }
        }
        finally { http.HttpContext = prior; }

        typeof(RoleEngine).GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
            .Should().NotContain(method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(ScopedRoleActor)),
                "a caller-supplied actor must not be part of the public mutation or plan surface");
    }

    [Fact]
    public async Task Generic_entity_mutation_cannot_bypass_the_engine()
    {
        var direct = async () => await new ScopedRoleDefinition
        {
            Id = Guid.NewGuid().ToString("N"), TenantId = "forged", ScopeType = "space", ScopeId = "x", Name = "Admin"
        }.Save();

        await direct.Should().ThrowAsync<EntityLifecycleCancelledException>()
            .Where(x => x.ReasonCode == "scoped-role.mutation.guard");

        var fastBulk = async () => await ScopedRoleDefinition.RemoveAll(RemoveStrategy.Fast);
        await fastBulk.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*requires lifecycle enforcement*");

        var instruction = async () => await Data<ScopedRoleDefinition, string>.Execute<int>(
            new Instruction("forged-write", Effect: DataOperationEffect.Write));
        await instruction.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*could bypass required lifecycle enforcement*");
    }

    [Fact]
    public async Task Cross_tenant_parent_and_unknown_capability_fail_closed()
    {
        using var scope = _fixture.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RoleEngine>();
        var owner = new ScopedRoleActor("owner:validation");
        var root = Ref("tenant-a", "space");
        await engine.Register(owner, new(root));

        var crossTenant = async () => await engine.Register(owner,
            new(Ref("tenant-b", "topic"), root));
        await crossTenant.Should().ThrowAsync<ScopedRoleValidationException>()
            .Where(x => x.Code == "scope.tenant.mismatch");

        var unknown = async () => await engine.Define(owner, new(root, "Bad", [new("discussion.unknown")]));
        await unknown.Should().ThrowAsync<ScopedRoleValidationException>()
            .Where(x => x.Code == "capability.unknown");
    }

    private static async Task<(ScopedRoleActor Owner, ScopedRoleScopeRef Root, ScopedRoleScopeRef Topic)> Tree(RoleEngine engine)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var owner = new ScopedRoleActor($"owner:{suffix}");
        var root = new ScopedRoleScopeRef($"tenant:{suffix}", "space", $"space:{suffix}");
        var topic = new ScopedRoleScopeRef(root.TenantId, "topic", $"topic:{suffix}");
        await engine.Register(owner, new(root));
        await engine.Register(owner, new(topic, root));
        return (owner, root, topic);
    }

    private static ScopedRoleScopeRef Ref(string tenant, string type)
        => new(tenant, type, Guid.NewGuid().ToString("N"));

    private sealed class FiniteAuthority : IScopedRoleAuthorityContributor
    {
        public DateTimeOffset? Maximum { get; init; }

        public ValueTask<IReadOnlyList<ScopedRoleAuthorityEnvelope>> Contribute(
            ScopedRoleAuthorityRequest request, CancellationToken ct = default)
            => Maximum is null
                ? ValueTask.FromResult<IReadOnlyList<ScopedRoleAuthorityEnvelope>>([])
                : ValueTask.FromResult<IReadOnlyList<ScopedRoleAuthorityEnvelope>>([
                    new(request.Target, new HashSet<ScopedRoleAuthorityOperation> { ScopedRoleAuthorityOperation.AssignRole },
                        MaximumExpiry: Maximum, AllowSelfAssignment: true, ProofKey: "finite", ProofVersion: 1)
                ]);

        public ValueTask<bool> Validate(ScopedRoleAuthorityRequest request,
            ScopedRoleAuthorityEnvelope envelope, CancellationToken ct = default)
            => ValueTask.FromResult(envelope.ProofKey == "finite" && envelope.ProofVersion == 1);
    }

    private sealed class CommitRejectAuthority : IScopedRoleAuthorityContributor
    {
        public bool Enabled { get; init; }
        public int Validations { get; private set; }

        public ValueTask<IReadOnlyList<ScopedRoleAuthorityEnvelope>> Contribute(
            ScopedRoleAuthorityRequest request, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<ScopedRoleAuthorityEnvelope>>(Enabled
                ? [new(request.Target, new HashSet<ScopedRoleAuthorityOperation> { request.Operation },
                    AllowSelfAssignment: true, ProofKey: "revoked", ProofVersion: 7)]
                : []);

        public ValueTask<bool> Validate(ScopedRoleAuthorityRequest request,
            ScopedRoleAuthorityEnvelope envelope, CancellationToken ct = default)
        {
            Validations++;
            return ValueTask.FromResult(false);
        }
    }

    private sealed class ClauseLimitedAuthority : IScopedRoleAuthorityContributor
    {
        private static readonly IReadOnlySet<ScopedRoleAuthorityOperation> Operations =
            Enum.GetValues<ScopedRoleAuthorityOperation>().ToHashSet();
        public IReadOnlyList<ScopedRoleGrantClause> Grants { get; init; } = [];
        public IReadOnlyList<ScopedRoleAudienceClause> Audience { get; init; } = [];

        public ValueTask<IReadOnlyList<ScopedRoleAuthorityEnvelope>> Contribute(
            ScopedRoleAuthorityRequest request, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<ScopedRoleAuthorityEnvelope>>([
                new ScopedRoleAuthorityEnvelope(request.Target, Operations, Descendants: true,
                    ProofKey: "clause-limited", ProofVersion: 1)
                {
                    GrantClauses = Grants,
                    AudienceClauses = Audience,
                }
            ]);

        public ValueTask<bool> Validate(ScopedRoleAuthorityRequest request,
            ScopedRoleAuthorityEnvelope envelope, CancellationToken ct = default)
            => ValueTask.FromResult(envelope.ProofKey == "clause-limited" && envelope.ProofVersion == 1);
    }

    private sealed class ResetPolicyAuthority : IScopedRoleAuthorityContributor
    {
        public ScopedRoleScopeRef? Scope { get; init; }
        public string? Capability { get; init; }
        public bool AllowReset { get; init; }
        public bool IncludeSupportedFallback { get; init; }
        public IReadOnlyList<ScopedRoleAudienceClause>? Audience { get; init; }

        public ValueTask<IReadOnlyList<ScopedRoleAuthorityEnvelope>> Contribute(
            ScopedRoleAuthorityRequest request, CancellationToken ct = default)
        {
            if (Scope is null || Capability is null || request.Actor.Subject != "steward:reset")
                return ValueTask.FromResult<IReadOnlyList<ScopedRoleAuthorityEnvelope>>([]);
            var operations = new HashSet<ScopedRoleAuthorityOperation>
            {
                AllowReset ? ScopedRoleAuthorityOperation.ResetPolicy : ScopedRoleAuthorityOperation.ManagePolicy,
            };
            var constrained = new ScopedRoleAuthorityEnvelope(Scope, operations,
                    Capabilities: new HashSet<string>([Capability], StringComparer.Ordinal),
                    ProofKey: "reset-policy", ProofVersion: 1)
                {
                    AudienceClauses = Audience,
                };
            if (!IncludeSupportedFallback)
                return ValueTask.FromResult<IReadOnlyList<ScopedRoleAuthorityEnvelope>>([constrained]);
            return ValueTask.FromResult<IReadOnlyList<ScopedRoleAuthorityEnvelope>>([
                constrained,
                new ScopedRoleAuthorityEnvelope(Scope, operations,
                    Capabilities: new HashSet<string>([Capability], StringComparer.Ordinal),
                    ProofKey: "reset-policy", ProofVersion: 1),
            ]);
        }

        public ValueTask<bool> Validate(ScopedRoleAuthorityRequest request,
            ScopedRoleAuthorityEnvelope envelope, CancellationToken ct = default)
            => ValueTask.FromResult(envelope.ProofKey == "reset-policy" && envelope.ProofVersion == 1);
    }
}

public sealed class ScopedDiscussionPost : Koan.Data.Core.Model.Entity<ScopedDiscussionPost>
{
    public string TenantId { get; set; } = "";
    public string TopicId { get; set; } = "";
    public string Body { get; set; } = "";
}

public sealed class LegacyScopedPost : Koan.Data.Core.Model.Entity<LegacyScopedPost>
{
    public string TopicId { get; set; } = "";
}
