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
    public async Task Reapproval_runs_member_lifecycle_veto_and_committed_success_events()
    {
        using var scope = _fixture.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RoleEngine>();
        var (owner, root, topic) = await Tree(engine);
        var role = await engine.Define(owner, new(root, "Reapproved reader", [new("discussion.read")]));
        var binding = await engine.Assign(owner, new(root, "participant:reapprove", role.Id,
            ScopedRolePropagation.Descendants));
        role = await engine.Edit(owner, new(role.Id, role.Version,
            Grants: [new("discussion.read"), new("discussion.reply")]));
        (await engine.Check(new("participant:reapprove"), "discussion.read", topic)).Should().BeFalse();
        var before = 0;
        var after = 0;
        var veto = true;
        ScopedRoleChangeContext? committed = null;
        Koan.Data.Core.Model.Entity.Role
            .MemberAdding(context =>
            {
                if (context.Subject != "participant:reapprove")
                    return ValueTask.FromResult(ScopedRoleChangeDecision.Continue());
                Interlocked.Increment(ref before);
                return ValueTask.FromResult(veto
                    ? ScopedRoleChangeDecision.Veto("test.reapprove.veto", "Reapproval was vetoed.")
                    : ScopedRoleChangeDecision.Continue());
            })
            .MemberAdded(context =>
            {
                if (context.Subject == "participant:reapprove")
                {
                    Interlocked.Increment(ref after);
                    committed = context;
                }
                return ValueTask.CompletedTask;
            });
        try
        {
            var rejected = async () => await engine.Reapprove(owner, binding.Id, binding.Version);
            await rejected.Should().ThrowAsync<ScopedRoleAuthorizationException>()
                .Where(error => error.Code == "test.reapprove.veto");
            after.Should().Be(0);
            (await ScopedRoleBinding.Get(binding.Id))!.Version.Should().Be(binding.Version);

            veto = false;
            var approved = await engine.Reapprove(owner, binding.Id, binding.Version);
            before.Should().Be(2);
            after.Should().Be(1);
            committed.Should().NotBeNull();
            committed!.Phase.Should().Be(ScopedRoleChangePhase.After);
            committed.Version.Should().Be(approved.Version);
            committed.Actor.Should().Be(owner);
            (await engine.Check(new("participant:reapprove"), "discussion.reply", topic)).Should().BeTrue();
        }
        finally { Koan.Data.Core.Model.Entity.Role.Reset(); }
    }

    [Fact]
    public async Task Reapproval_lifecycle_sees_policy_derived_effective_grants()
    {
        using var scope = _fixture.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RoleEngine>();
        var (owner, root, topic) = await Tree(engine);
        var badge = await engine.Define(owner, new(root, "Policy badge", []));
        var binding = await engine.Assign(owner, new(root, "participant:policy-badge", badge.Id,
            ScopedRolePropagation.Descendants));
        await engine.Replace(owner, new(root, "discussion.reply", [
            new(ScopedRoleAudienceKind.Role, badge.Id),
        ]));
        (await engine.Check(new("participant:policy-badge"), "discussion.reply", topic)).Should().BeFalse(
            "the new policy authority remains unavailable until the stale binding is explicitly reapproved");

        var veto = true;
        var after = 0;
        ScopedRoleChangeContext? committed = null;
        Koan.Data.Core.Model.Entity.Role
            .MemberAdding(context =>
            {
                if (context.Subject != "participant:policy-badge")
                    return ValueTask.FromResult(ScopedRoleChangeDecision.Continue());
                context.CurrentPermissions.Select(grant => grant.Capability)
                    .Should().Contain("discussion.reply");
                return ValueTask.FromResult(veto
                    ? ScopedRoleChangeDecision.Veto("test.policy-grant.veto", "Policy-derived authority was vetoed.")
                    : ScopedRoleChangeDecision.Continue());
            })
            .MemberAdded(context =>
            {
                if (context.Subject == "participant:policy-badge")
                {
                    Interlocked.Increment(ref after);
                    committed = context;
                }
                return ValueTask.CompletedTask;
            });
        try
        {
            var rejected = async () => await engine.Reapprove(owner, binding.Id, binding.Version);
            await rejected.Should().ThrowAsync<ScopedRoleAuthorizationException>()
                .Where(error => error.Code == "test.policy-grant.veto");
            after.Should().Be(0, "a vetoed reapproval cannot emit the committed lifecycle event");
            (await ScopedRoleBinding.Get(binding.Id))!.Version.Should().Be(binding.Version);

            veto = false;
            var approved = await engine.Reapprove(owner, binding.Id, binding.Version);
            after.Should().Be(1);
            committed.Should().NotBeNull();
            committed!.CurrentPermissions.Select(grant => grant.Capability)
                .Should().Contain("discussion.reply");
            committed.Version.Should().Be(approved.Version);
            (await engine.Check(new("participant:policy-badge"), "discussion.reply", topic)).Should().BeTrue();
        }
        finally { Koan.Data.Core.Model.Entity.Role.Reset(); }
    }

    [Fact]
    public async Task Role_management_inputs_reject_unbounded_text_collections_and_parameter_graphs()
    {
        using var scope = _fixture.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RoleEngine>();
        var (owner, root, topic) = await Tree(engine);

        await FluentActions.Awaiting(() => engine.Define(owner, new(root,
                new string('n', ScopedRoleInputLimits.NameLength + 1), [])))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => engine.Define(owner, new(root, "Bounded", [],
                Purpose: new string('p', ScopedRoleInputLimits.DescriptionLength + 1))))
            .Should().ThrowAsync<ArgumentException>();
        var presentation = Enumerable.Range(0, ScopedRoleInputLimits.PresentationEntries + 1)
            .ToDictionary(index => $"key:{index}", _ => "value", StringComparer.Ordinal);
        await FluentActions.Awaiting(() => engine.Define(owner, new(root, "Bounded", [],
                Presentation: presentation)))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => engine.Define(owner, new(root, "Bounded", [],
                Presentation: new Dictionary<string, string>
                {
                    [new string('k', ScopedRoleInputLimits.PresentationKeyLength + 1)] = "value",
                })))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => engine.Define(owner, new(root, "Bounded", [],
                Presentation: new Dictionary<string, string>
                {
                    ["key"] = new string('v', ScopedRoleInputLimits.PresentationValueLength + 1),
                })))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => engine.Define(owner, new(root, "Conditional", [new("discussion.approve", [
                new("amount", ScopedRoleConditionOperator.LessThanOrEqual,
                    new string('1', ScopedRoleInputLimits.ParameterValueLength + 1)),
            ])])))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => engine.Define(owner, new(root, "Conditional", [new("discussion.approve", [
                new(new string('p', ScopedRoleInputLimits.ParameterNameLength + 1),
                    ScopedRoleConditionOperator.Equal, "value"),
            ])])))
            .Should().ThrowAsync<ArgumentException>();

        var nested = new Dictionary<string, object?> { ["amount"] = new { Value = 10 } };
        await FluentActions.Awaiting(() => engine.Plan(new("participant:bounded"),
                "discussion.approve", topic, nested))
            .Should().ThrowAsync<ArgumentException>();
        var tooManyParameters = Enumerable.Range(0, ScopedRoleInputLimits.Parameters + 1)
            .ToDictionary(index => $"parameter:{index}", index => (object?)index, StringComparer.Ordinal);
        await FluentActions.Awaiting(() => engine.Plan(new("participant:bounded"),
                "discussion.approve", topic, tooManyParameters))
            .Should().ThrowAsync<ArgumentException>();
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
        var snapshots = scope.ServiceProvider.GetRequiredService<ScopedRoleSnapshotCache>();
        var steward = new ScopedRoleActor("steward:reset");
        var manageOnly = new ResetPolicyAuthority
        {
            Scope = topic,
            Capability = "discussion.reply",
            Audience = [new(ScopedRoleAudienceKind.Role, member.Id)],
        };
        var limited = new RoleEngine(catalog, [manageOnly], [],
            Microsoft.Extensions.Options.Options.Create(new RoleEngineOptions()), null, null, null, snapshots);

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
        }], [], Microsoft.Extensions.Options.Options.Create(new RoleEngineOptions()), null, null, null, snapshots);
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
        }], [], Microsoft.Extensions.Options.Options.Create(new RoleEngineOptions()), null, null, null, snapshots);
        var inherited = await reset.Inherit(steward, topic, "discussion.reply", narrowing.Version);
        inherited.Mode.Should().Be(ScopedRoleOverrideMode.Inherit);
        (await normal.Check(new("participant:reset"), "discussion.reply", topic)).Should().BeTrue();
    }

    [Fact]
    public async Task Compiled_membership_sets_invalidate_on_add_remove_readd_and_role_events()
    {
        using var scope = _fixture.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RoleEngine>();
        var snapshots = scope.ServiceProvider.GetRequiredService<ScopedRoleSnapshotCache>();
        var (owner, root, topic) = await Tree(engine);
        var reader = await engine.Define(owner, new(root, "Reader", [new("discussion.read")]));
        var adding = 0;
        var added = 0;
        var removed = 0;
        var permissionsChanged = 0;
        var postRemovalDenied = false;
        ScopedRoleChangeContext? addedContext = null;
        ScopedRoleChangeContext? permissionContext = null;
        Koan.Data.Core.Model.Entity.Role
            .MemberAdding(context =>
            {
                if (context.Scope.TenantId != root.TenantId) return ValueTask.FromResult(ScopedRoleChangeDecision.Continue());
                Interlocked.Increment(ref adding);
                return ValueTask.FromResult(context.Subject == "participant:veto"
                    ? ScopedRoleChangeDecision.Veto("test.role.veto", "The test vetoed this membership.")
                    : ScopedRoleChangeDecision.Continue());
            })
            .MemberAdded(context =>
            {
                if (context.Scope.TenantId == root.TenantId)
                {
                    Interlocked.Increment(ref added);
                    addedContext = context;
                }
                return ValueTask.CompletedTask;
            })
            .MemberRemoved(async context =>
            {
                if (context.Scope.TenantId != root.TenantId) return;
                Interlocked.Increment(ref removed);
                postRemovalDenied = !await engine.Check(new("participant:cycle"), "discussion.read", topic);
            })
            .MemberRemoved(context => context.Subject == "participant:post-fail"
                ? ValueTask.FromException(new InvalidOperationException("post failure"))
                : ValueTask.CompletedTask)
            .PermissionsChanged(context =>
            {
                if (context.Scope.TenantId == root.TenantId)
                {
                    Interlocked.Increment(ref permissionsChanged);
                    permissionContext = context;
                }
                return ValueTask.CompletedTask;
            });
        try
        {
            var vetoed = async () => await engine.Assign(owner, new(root, "participant:veto", reader.Id,
                ScopedRolePropagation.Descendants));
            await vetoed.Should().ThrowAsync<ScopedRoleAuthorizationException>()
                .Where(error => error.Code == "test.role.veto");
            (await ScopedRoleBinding.Get(ScopedRoleBinding.KeyFor(root.TenantId, "participant:veto", reader.Id, root)))
                .Should().BeNull();
            added.Should().Be(0);

            var first = await engine.Assign(owner, new(root, "participant:cycle", reader.Id,
                ScopedRolePropagation.Descendants));
            var eventsBeforeRetry = (adding, added);
            var retry = await engine.Assign(owner, new(root, "participant:cycle", reader.Id,
                ScopedRolePropagation.Descendants));
            retry.Id.Should().Be(first.Id);
            retry.Version.Should().Be(first.Version, "a repeated live collection add is idempotent");
            (adding, added).Should().Be(eventsBeforeRetry, "an idempotent add is not a new lifecycle change");
            addedContext.Should().NotBeNull();
            addedContext!.Actor.Should().Be(owner);
            addedContext.Subject.Should().Be("participant:cycle");
            addedContext.Scope.Should().Be(root);
            addedContext.Phase.Should().Be(ScopedRoleChangePhase.After);
            addedContext.Version.Should().Be(first.Version);
            var firstVersion = first.Version;
            (await engine.Check(new("participant:cycle"), "discussion.read", topic)).Should().BeTrue();
            var warmBuilds = snapshots.BuildCount;
            (await engine.Check(new("participant:cycle"), "discussion.read", topic)).Should().BeTrue();
            snapshots.BuildCount.Should().Be(warmBuilds, "a warm check must reuse the compiled scope snapshot");

            var removedBinding = await engine.Revoke(owner, first.Id, first.Version);
            removedBinding.Revoked.Should().BeTrue();
            (await ScopedRoleBinding.Get(first.Id)).Should().BeNull("membership removal deletes the collection row");
            postRemovalDenied.Should().BeTrue("cache invalidation precedes successful post-events");
            (await engine.Check(new("participant:cycle"), "discussion.read", topic)).Should().BeFalse();

            var second = await engine.Assign(owner, new(root, "participant:cycle", reader.Id,
                ScopedRolePropagation.Descendants));
            second.Id.Should().Be(first.Id);
            second.Version.Should().BeGreaterThan(firstVersion, "re-add must not recreate a stale ETag generation");
            (await engine.Check(new("participant:cycle"), "discussion.read", topic)).Should().BeTrue();
            var beforeDomainChange = snapshots.BuildCount;
            scope.ServiceProvider.GetRequiredService<IScopedRoleAccessInvalidator>()
                .Invalidate(new(topic, "test:membership", 1));
            (await engine.Check(new("participant:cycle"), "discussion.read", topic)).Should().BeTrue();
            snapshots.BuildCount.Should().Be(beforeDomainChange + 1);

            var failing = await engine.Assign(owner, new(root, "participant:post-fail", reader.Id,
                ScopedRolePropagation.Descendants));
            _ = await engine.Check(new("participant:post-fail"), "discussion.read", topic);
            var postFailure = async () => await engine.Revoke(owner, failing.Id, failing.Version);
            await postFailure.Should().ThrowAsync<ScopedRolePostEventException>();
            (await ScopedRoleBinding.Get(failing.Id)).Should().BeNull("post failures happen after durable removal");
            (await engine.Check(new("participant:post-fail"), "discussion.read", topic)).Should().BeFalse();

            reader = await engine.Edit(owner, new(reader.Id, reader.Version,
                Grants: [new("discussion.reply")]));
            permissionsChanged.Should().Be(1);
            permissionContext.Should().NotBeNull();
            permissionContext!.Actor.Should().Be(owner);
            permissionContext.PreviousPermissions.Select(grant => grant.Capability)
                .Should().Equal("discussion.read");
            permissionContext.CurrentPermissions.Select(grant => grant.Capability)
                .Should().Equal("discussion.reply");
            permissionContext.Version.Should().Be(reader.Version);
            permissionContext.Timestamp.Should().NotBe(default);
            (await engine.Check(new("participant:cycle"), "discussion.read", topic)).Should().BeFalse();

            var postsBeforeUnauthorized = added;
            var unauthorized = async () => await engine.Assign(new ScopedRoleActor("participant:ordinary"),
                new(root, "participant:other", reader.Id));
            await unauthorized.Should().ThrowAsync<ScopedRoleAuthorizationException>();
            added.Should().Be(postsBeforeUnauthorized);

            adding.Should().Be(4);
            added.Should().Be(3);
            removed.Should().Be(2);
        }
        finally { Koan.Data.Core.Model.Entity.Role.Reset(); }
    }

    [Fact]
    public async Task Private_topic_audience_matches_namespaced_gardeners_or_admin_memberships()
    {
        using var scope = _fixture.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RoleEngine>();
        var (owner, root, topic) = await Tree(engine);
        var admin = await engine.Define(owner, new(root, "Admin", [new("discussion.read")], Id: "role:admin"));
        var gardeners = await engine.Define(owner, new(root, "Gardeners", [], Id: "group:gardeners"));
        await engine.Replace(owner, new(topic, "discussion.read", [
            new(ScopedRoleAudienceKind.Role, gardeners.Id),
            new(ScopedRoleAudienceKind.Role, admin.Id),
        ]));
        await engine.Assign(owner, new(root, "alice:garden", gardeners.Id, ScopedRolePropagation.Descendants));
        await engine.Assign(owner, new(root, "alice:admin", admin.Id, ScopedRolePropagation.Descendants));

        var gardenerAtTopic = await engine.Plan(new("alice:garden"), "discussion.read", topic);
        gardenerAtTopic.Memberships.ContainsAny("role:admin", "group:gardeners").Should().BeTrue();
        gardenerAtTopic.Audience.Matches(gardenerAtTopic.Memberships).Should().BeTrue();
        gardenerAtTopic.Memberships.Contains("permission:discussion.read").Should().BeFalse(
            "group membership can match an audience but does not grant a capability");
        gardenerAtTopic.Allowed.Should().BeTrue();

        var adminAtTopic = await engine.Plan(new("alice:admin"), "discussion.read", topic);
        adminAtTopic.Memberships.ContainsAny("role:admin", "group:gardeners").Should().BeTrue();
        adminAtTopic.Memberships.Contains("permission:discussion.read").Should().BeTrue(
            "permission tokens are derived from the compiled role grant");
        adminAtTopic.Audience.Matches(adminAtTopic.Memberships).Should().BeTrue();
        adminAtTopic.Allowed.Should().BeTrue();

        var gardenerAtRoot = await engine.Plan(new("alice:garden"), "discussion.read", root);
        gardenerAtRoot.Allowed.Should().BeFalse("the group audience is scoped to the private topic");
        (await engine.Check(new("alice:outside"), "discussion.read", topic)).Should().BeFalse();

        var forgedPermission = () => ScopedRoleMembershipSet.Create("permission:discussion.read");
        forgedPermission.Should().Throw<ArgumentException>();
        var capabilityGroup = async () => await engine.Define(owner,
            new(root, "Invalid group", [new("discussion.read")], Id: "group:capability-owner"));
        await capabilityGroup.Should().ThrowAsync<ScopedRoleValidationException>()
            .Where(error => error.Code == "group.capabilities.unsupported");
    }

    [Fact]
    public async Task Tangent_owner_can_be_promoted_demoted_and_promoted_with_the_same_binding_identity()
    {
        using var scope = _fixture.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RoleEngine>();
        var (owner, root, topic) = await Tree(engine);
        var ownerRole = await engine.Define(owner,
            new(root, "Owner", [new("discussion.read")], Id: "role:owner"));

        var firstPromotion = await engine.Assign(owner, new(root, "tangent:owner", ownerRole.Id,
            ScopedRolePropagation.Descendants));
        (await engine.Check(new("tangent:owner"), "discussion.read", topic)).Should().BeTrue();
        await engine.Revoke(owner, firstPromotion.Id, firstPromotion.Version);
        (await ScopedRoleBinding.Get(firstPromotion.Id)).Should().BeNull();
        (await engine.Remove(owner, new(root, "tangent:owner", ownerRole.Id))).Should().BeFalse(
            "removing an absent collection member is an idempotent no-op");
        (await engine.Check(new("tangent:owner"), "discussion.read", topic)).Should().BeFalse();

        var secondPromotion = await engine.Assign(owner, new(root, "tangent:owner", ownerRole.Id,
            ScopedRolePropagation.Descendants));
        secondPromotion.Id.Should().Be(firstPromotion.Id);
        secondPromotion.Version.Should().BeGreaterThan(firstPromotion.Version);
        (await engine.Check(new("tangent:owner"), "discussion.read", topic)).Should().BeTrue();
    }

    [Fact]
    public async Task Concurrent_collection_changes_publish_one_success_and_keep_snapshots_immutable()
    {
        using var scope = _fixture.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RoleEngine>();
        var snapshots = scope.ServiceProvider.GetRequiredService<ScopedRoleSnapshotCache>();
        var (owner, root, topic) = await Tree(engine);
        var role = await engine.Define(owner, new(root, "Concurrent reader", [new("discussion.read")]));
        var addArrivals = 0;
        var addSuccesses = 0;
        var bothAdding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var removeArrivals = 0;
        var removeSuccesses = 0;
        var bothRemoving = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Koan.Data.Core.Model.Entity.Role
            .MemberAdding(async context =>
            {
                if (context.Subject != "participant:concurrent") return ScopedRoleChangeDecision.Continue();
                if (Interlocked.Increment(ref addArrivals) == 2) bothAdding.TrySetResult();
                await bothAdding.Task;
                return ScopedRoleChangeDecision.Continue();
            })
            .MemberAdded(context =>
            {
                if (context.Subject == "participant:concurrent") Interlocked.Increment(ref addSuccesses);
                return ValueTask.CompletedTask;
            })
            .MemberRemoving(async context =>
            {
                if (context.Subject != "participant:concurrent") return ScopedRoleChangeDecision.Continue();
                if (Interlocked.Increment(ref removeArrivals) == 2) bothRemoving.TrySetResult();
                await bothRemoving.Task;
                return ScopedRoleChangeDecision.Continue();
            })
            .MemberRemoved(context =>
            {
                if (context.Subject == "participant:concurrent") Interlocked.Increment(ref removeSuccesses);
                return ValueTask.CompletedTask;
            });
        try
        {
            var additions = await Task.WhenAll(
                engine.Assign(owner, new(root, "participant:concurrent", role.Id, ScopedRolePropagation.Descendants)),
                engine.Assign(owner, new(root, "participant:concurrent", role.Id, ScopedRolePropagation.Descendants)));
            additions.Select(binding => binding.Id).Distinct().Should().ContainSingle();
            addSuccesses.Should().Be(1);
            (await ScopedRoleBinding.Query(binding => binding.Subject == "participant:concurrent"))
                .Should().ContainSingle();

            var before = await engine.Plan(new("participant:concurrent"), "discussion.read", topic);
            before.Allowed.Should().BeTrue();
            before.Memberships.Contains(ScopedRoleTokens.RoleOrGroup(role.Id)).Should().BeTrue();
            var warmBuilds = snapshots.BuildCount;
            _ = await engine.Plan(new("participant:concurrent"), "discussion.read", topic);
            snapshots.BuildCount.Should().Be(warmBuilds);
            var key = new ScopedRoleSnapshotCache.CacheKey(topic.TenantId, topic.Type, topic.Id);
            var compiled = await snapshots.Get(key, _ => throw new InvalidOperationException("warm snapshot rebuilt"),
                CancellationToken.None);
            compiled.SubjectRoles["participant:concurrent"].Should().Contain(role.Id);
            compiled.RoleMembers[role.Id].Should().Contain("participant:concurrent");

            var removals = await Task.WhenAll(
                engine.Revoke(owner, additions[0].Id, additions[0].Version),
                engine.Revoke(owner, additions[0].Id, additions[0].Version));
            removals.Should().OnlyContain(binding => binding.Revoked);
            removeSuccesses.Should().Be(1);
            (await ScopedRoleBinding.Get(additions[0].Id)).Should().BeNull();

            var after = await engine.Plan(new("participant:concurrent"), "discussion.read", topic);
            after.Allowed.Should().BeFalse();
            snapshots.BuildCount.Should().BeGreaterThan(warmBuilds, "removal forces a cold immutable rebuild");
            before.Memberships.Contains(ScopedRoleTokens.RoleOrGroup(role.Id)).Should().BeTrue(
                "readers holding the prior immutable snapshot are not mutated in place");
            after.Memberships.Contains(ScopedRoleTokens.RoleOrGroup(role.Id)).Should().BeFalse();
        }
        finally { Koan.Data.Core.Model.Entity.Role.Reset(); }
    }

    [Fact]
    public async Task Stale_revoke_cannot_delete_a_concurrently_reapproved_membership_generation()
    {
        using var scope = _fixture.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RoleEngine>();
        var (owner, root, topic) = await Tree(engine);
        var role = await engine.Define(owner, new(root, "CAS reader", [new("discussion.read")]));
        var binding = await engine.Assign(owner, new(root, "participant:delete-cas", role.Id,
            ScopedRolePropagation.Descendants));
        var removalEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRemoval = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Koan.Data.Core.Model.Entity.Role.MemberRemoving(async context =>
        {
            if (context.Subject != "participant:delete-cas") return ScopedRoleChangeDecision.Continue();
            removalEntered.TrySetResult();
            await releaseRemoval.Task;
            return ScopedRoleChangeDecision.Continue();
        });
        try
        {
            var staleRemoval = engine.Revoke(owner, binding.Id, binding.Version);
            await removalEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var reapproved = await engine.Reapprove(owner, binding.Id, binding.Version);
            releaseRemoval.TrySetResult();

            await FluentActions.Awaiting(() => staleRemoval).Should().ThrowAsync<ScopedRoleConcurrencyException>();
            var stored = await ScopedRoleBinding.Get(binding.Id);
            stored.Should().NotBeNull();
            stored!.Version.Should().Be(reapproved.Version);
            (await engine.Check(new("participant:delete-cas"), "discussion.read", topic)).Should().BeTrue();
        }
        finally
        {
            releaseRemoval.TrySetResult();
            Koan.Data.Core.Model.Entity.Role.Reset();
        }
    }

    [Fact]
    public async Task Compiled_snapshot_expires_and_an_expired_membership_can_be_added_again()
    {
        using var scope = _fixture.Services.CreateScope();
        var normal = scope.ServiceProvider.GetRequiredService<RoleEngine>();
        var (owner, root, topic) = await Tree(normal);
        var role = await normal.Define(owner, new(root, "Temporary reader", [new("discussion.read")]));
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var options = Microsoft.Extensions.Options.Options.Create(new RoleEngineOptions());
        var snapshots = new ScopedRoleSnapshotCache(options.Value, clock);
        var engine = new RoleEngine(scope.ServiceProvider.GetRequiredService<ScopedRoleCatalog>(),
            scope.ServiceProvider.GetServices<IScopedRoleAuthorityContributor>(), [], options,
            null, null, clock, snapshots);
        var binding = await engine.Assign(owner, new(root, "participant:expiring", role.Id,
            ScopedRolePropagation.Descendants, clock.GetUtcNow().AddMinutes(5)));
        (await engine.Check(new("participant:expiring"), "discussion.read", topic)).Should().BeTrue();
        var warmBuilds = snapshots.BuildCount;

        clock.Advance(TimeSpan.FromMinutes(6));
        (await engine.Check(new("participant:expiring"), "discussion.read", topic)).Should().BeFalse();
        snapshots.BuildCount.Should().Be(warmBuilds + 1, "expiry is a hard snapshot validity boundary");

        var renewed = await engine.Assign(owner, new(root, "participant:expiring", role.Id,
            ScopedRolePropagation.Descendants, clock.GetUtcNow().AddMinutes(5)));
        renewed.Id.Should().Be(binding.Id);
        renewed.Version.Should().BeGreaterThan(binding.Version);
        (await engine.Check(new("participant:expiring"), "discussion.read", topic)).Should().BeTrue();
    }

    [Fact]
    public async Task Invalidation_during_compilation_never_returns_the_evicted_generation()
    {
        var options = new RoleEngineOptions();
        var cache = new ScopedRoleSnapshotCache(options, TimeProvider.System);
        var target = Ref("tenant:compile-race", "topic");
        var key = new ScopedRoleSnapshotCache.CacheKey(target.TenantId, target.Type, target.Id);
        var firstBuildStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstBuild = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var generation = 0;

        async Task<ScopedRoleSnapshotCache.CompiledSnapshot> Build(CancellationToken ct)
        {
            var current = Interlocked.Increment(ref generation);
            if (current == 1)
            {
                firstBuildStarted.SetResult();
                await releaseFirstBuild.Task.WaitAsync(ct);
            }
            return new(key, [target],
                new Dictionary<string, IReadOnlyList<ScopedRoleGrantClause>>(StringComparer.Ordinal),
                new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal),
                new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal),
                new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal),
                new Dictionary<string, ScopedRoleMembershipSet>(StringComparer.Ordinal),
                new Dictionary<string, ScopedRoleAudience>(StringComparer.Ordinal),
                new Dictionary<string, ScopedRoleSnapshotCache.CompiledPolicy>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal),
                new Dictionary<string, long>(StringComparer.Ordinal) { ["generation"] = current },
                new Dictionary<string, long>(StringComparer.Ordinal),
                new Dictionary<string, long>(StringComparer.Ordinal),
                new Dictionary<string, IReadOnlyDictionary<string, long>>(StringComparer.Ordinal), null);
        }

        var pending = cache.Get(key, Build, CancellationToken.None);
        await firstBuildStarted.Task;
        cache.InvalidateScope(target);
        releaseFirstBuild.SetResult();

        var snapshot = await pending;
        generation.Should().Be(2);
        snapshot.ScopeVersions["generation"].Should().Be(2);
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

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
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
