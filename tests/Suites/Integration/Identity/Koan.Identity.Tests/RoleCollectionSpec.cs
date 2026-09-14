using AwesomeAssertions;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Identity.Tests;

[Collection("identity")]
public sealed class RoleCollectionSpec : IdentityHostScopedSpec
{
    private readonly IdentityHostFixture _fixture;
    public RoleCollectionSpec(IdentityHostFixture fixture) : base(fixture) => _fixture = fixture;
    private RoleCollection Roles => _fixture.Services.GetRequiredService<RoleCollection>();

    [Fact] public void Intersection_is_pure_and_any_of()
    {
        var bag = new RoleBag("person:one", true, ["global:topic_read"]);
        Role.CanDo(PermissionCriteria.Any("global:no", "global:topic_read"), bag).Should().BeTrue();
        Role.CanDo(PermissionCriteria.Any("global:no"), bag).Should().BeFalse();
    }

    [Fact] public async Task Membership_compiles_role_key_and_unchanged_global_permissions()
    {
        var key = $"group:gardeners:{Guid.NewGuid():N}";
        await Roles.Define(key, "Gardeners", ["global:topic_read", "local:ignored"]);
        await Roles.Add(key, "person:garden");
        var bag = await Roles.Bag("person:garden", true);
        bag.Tokens.Should().Contain(key).And.Contain("global:topic_read").And.NotContain("permission:global:topic_read").And.NotContain("local:ignored");
    }

    [Fact] public async Task Rename_preserves_key_and_invalidates_affected_bag()
    {
        var key = $"role:member:{Guid.NewGuid():N}";
        await Roles.Define(key, "Member", ["global:post_create"]);
        await Roles.Add(key, "person:rename");
        var first = await Roles.Bag("person:rename", true);
        await Roles.Rename(key, "Community member");
        var second = await Roles.Bag("person:rename", true);
        second.Should().NotBeSameAs(first);
        second.Tokens.Should().Contain(key);
        (await Roles.Get(key))!.Name.Should().Be("Community member");
    }

    [Fact] public async Task Anonymous_and_authenticated_tokens_are_stable()
    {
        (await Roles.Bag(null, false)).Tokens.Should().ContainSingle().Which.Should().Be(RoleTokens.Everyone);
        (await Roles.Bag("person:auth", true)).Tokens.Should().Contain([RoleTokens.Everyone, RoleTokens.Authenticated]);
    }

    [Fact] public async Task Hot_bag_is_reused_without_membership_fetch()
    {
        var first = await Roles.Bag("person:hot", true);
        (await Roles.Bag("person:hot", true)).Should().BeSameAs(first);
    }

    [Fact] public void Loaded_resource_hook_selects_criteria_from_entity()
    {
        var post = new TestPost("global:topic_read");
        var bag = new RoleBag("person:reader", true, ["global:topic_read"]);
        RoleResourceAuthorization.CanDo(post, bag, value => PermissionCriteria.Any(value.ReadPermission)).Should().BeTrue();
    }

    [Fact] public async Task Actual_member_mutation_emits_entity_role_events_once()
    {
        var key = $"role:event:{Guid.NewGuid():N}";
        var before = 0; var after = 0;
        Koan.Data.Core.Model.Entity.Role.Reset()
            .MemberAdding(_ => { before++; return ValueTask.FromResult(RoleChangeDecision.Continue()); })
            .MemberAdded(_ => { after++; return ValueTask.CompletedTask; });
        try
        {
            await Roles.Define(key, "Event role", []);
            await Roles.Add(key, "person:event");
            await Roles.Add(key, "person:event");
            before.Should().Be(1); after.Should().Be(1);
        }
        finally { Koan.Data.Core.Model.Entity.Role.Reset(); }
    }
    private sealed record TestPost(string ReadPermission);
}
