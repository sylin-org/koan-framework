using AwesomeAssertions;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Lifecycle;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
        var bag = new RoleBag("person:one", true, [], ["global:topic_read"]);
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

    [Fact] public async Task Rename_preserves_the_key_and_leaves_bags_valid()
    {
        var key = $"role:member:{Guid.NewGuid():N}";
        await Roles.Define(key, "Member", ["global:post_create"]);
        await Roles.Add(key, "person:rename");
        var first = await Roles.Bag("person:rename", true);
        await Roles.Rename(key, "Community member");
        (await Roles.Bag("person:rename", true)).Should().BeSameAs(first, "a bag holds keys and permissions, and a rename changes neither");
        first.Tokens.Should().Contain(key);
        (await Roles.Get(key))!.Name.Should().Be("Community member");
    }

    [Fact] public async Task A_permission_change_reaches_every_member_without_reading_the_members()
    {
        var key = $"role:permission-change:{Guid.NewGuid():N}";
        await Roles.Define(key, "Changing", ["global:before"]);
        await Roles.Add(key, "person:change-a");
        await Roles.Add(key, "person:change-b");
        var a = await Roles.Bag("person:change-a", true);
        (await Roles.Bag("person:change-b", true)).Permissions.Should().Contain("global:before");

        await Roles.SetPermissions(key, ["global:after"]);

        (await Roles.Bag("person:change-a", true)).Should().NotBeSameAs(a);
        (await Roles.Bag("person:change-a", true)).Permissions.Should().Contain("global:after").And.NotContain("global:before");
        (await Roles.Bag("person:change-b", true)).Permissions.Should().Contain("global:after").And.NotContain("global:before");
    }

    [Fact] public async Task Anonymous_and_authenticated_tokens_are_stable()
    {
        (await Roles.Bag(null, false)).Tokens.Should().ContainSingle().Which.Should().Be(RoleTokens.Everyone);
        (await Roles.Bag("person:auth", true)).Tokens.Should().Contain([RoleTokens.Everyone, RoleTokens.Authenticated]);
    }

    [Fact] public async Task Everyone_role_permissions_compile_into_every_bag_and_its_change_reaches_cached_bags()
    {
        try
        {
            await Roles.Define(RoleTokens.Everyone, "Everyone", ["global:everyone_read"]);
            var person = await Roles.Bag("person:everyone-bag", true);
            var visitor = await Roles.Bag(null, false);
            person.Tokens.Should().Contain("global:everyone_read");
            visitor.Tokens.Should().Contain("global:everyone_read");

            await Roles.SetPermissions(RoleTokens.Everyone, ["global:everyone_write"]);
            (await Roles.Bag("person:everyone-bag", true)).Tokens.Should().Contain("global:everyone_write").And.NotContain("global:everyone_read");
            (await Roles.Bag(null, false)).Tokens.Should().Contain("global:everyone_write").And.NotContain("global:everyone_read");
        }
        finally { await Roles.Delete(RoleTokens.Everyone); }
        (await Roles.Bag(null, false)).Tokens.Should().ContainSingle().Which.Should().Be(RoleTokens.Everyone);
    }

    [Fact] public async Task Hot_bag_is_reused_without_membership_fetch()
    {
        var first = await Roles.Bag("person:hot", true);
        (await Roles.Bag("person:hot", true)).Should().BeSameAs(first);
    }

    [Fact] public void Loaded_resource_hook_selects_criteria_from_entity()
    {
        var post = new TestPost("global:topic_read");
        var bag = new RoleBag("person:reader", true, [], ["global:topic_read"]);
        RoleResourceAuthorization.CanDo(post, bag, value => PermissionCriteria.Any(value.ReadPermission)).Should().BeTrue();
    }

    [Fact] public async Task Actual_member_mutation_emits_entity_role_events_once()
    {
        var key = $"role:event:{Guid.NewGuid():N}";
        var adding = 0; var added = 0; var removing = 0; var removed = 0;
        Koan.Data.Core.Model.Entity.Role.Reset()
            .MemberAdding(context => { adding++; context.Subject.Should().Be("person:event"); return ValueTask.FromResult(RoleChangeDecision.Continue()); })
            .MemberAdded(_ => { added++; return ValueTask.CompletedTask; })
            .MemberRemoving(_ => { removing++; return ValueTask.FromResult(RoleChangeDecision.Continue()); })
            .MemberRemoved(_ => { removed++; return ValueTask.CompletedTask; });
        try
        {
            await Roles.Define(key, "Event role", []);
            var member = await Roles.Add(key, "person:event");
            (await Roles.Add(key, "person:event")).Id.Should().Be(member.Id, "adding again returns the same membership");
            (await Roles.Remove(key, "person:event")).Should().BeTrue();
            (await Roles.Remove(key, "person:event")).Should().BeFalse("they are no longer a member");
            (adding, added, removing, removed).Should().Be((1, 1, 1, 1));
        }
        finally { Koan.Data.Core.Model.Entity.Role.Reset(); }
    }

    /// <summary>A person's roles are one read of their membership rows by person; a role's other members do not
    /// enter it.</summary>
    [Fact] public async Task A_persons_roles_are_read_through_the_person_index()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var person = $"person:holder:{suffix}";
        var held = new[] { $"role:held-a:{suffix}", $"role:held-b:{suffix}" };
        foreach (var key in held.Append($"role:not-held:{suffix}")) await Roles.Define(key, key, ["global:read"]);
        foreach (var key in held) await Roles.Add(key, person);
        for (var i = 0; i < 20; i++) await Roles.Add($"role:not-held:{suffix}", $"person:other:{suffix}:{i}");

        var indexes = Koan.Data.Core.IndexMetadata.GetIndexes(typeof(RoleMember))
            .Where(index => !index.IsPrimaryKey).Select(index => string.Join(",", index.Properties.Select(property => property.Name))).ToList();
        indexes.Should().Contain(nameof(RoleMember.PersonId), "a person's memberships are read by person");
        indexes.Should().Contain($"{nameof(RoleMember.RoleKey)},{nameof(RoleMember.PersonId)}", "a role's members are read by role, then person");

        (await Roles.ForPerson(person)).Select(role => role.Key).Should().Equal(held);
        (await Roles.Bag(person, true)).Roles.Should().Contain(held).And.NotContain($"role:not-held:{suffix}");
    }

    /// <summary>A role's members are read a page at a time in person order, each page continuing from the last
    /// person the previous one returned, so every member is read exactly once.</summary>
    [Fact] public async Task A_roles_members_page_with_a_cursor()
    {
        var key = $"role:paged:{Guid.NewGuid():N}";
        await Roles.Define(key, "Paged", []);
        var people = Enumerable.Range(0, 23).Select(i => $"person:paged:{i:D2}").ToArray();
        foreach (var person in people.Reverse()) await Roles.Add(key, person);

        var seen = new List<string>();
        var pages = 0;
        for (string? after = null; ;)
        {
            var page = await Roles.Members(key, after, take: 5);
            page.Items.Should().HaveCountLessThanOrEqualTo(5);
            page.Items.Should().OnlyContain(member => member.RoleKey == key);
            seen.AddRange(page.Items.Select(member => member.PersonId));
            pages++;
            if ((after = page.Next) is null) break;
            RoleReads.ParseCursor(after).Should().Be((page.Items[^1].PersonId, page.Items[^1].Id), "the cursor is the last row's own key and id");
        }

        seen.Should().Equal(people, "each member once, in person order");
        pages.Should().Be(5);

        (await Roles.Remove(key, people[7])).Should().BeTrue();
        var continued = await Roles.Members(key, RoleReads.Cursor(people[6], RoleMember.KeyFor(key, people[6])), take: 2);
        continued.Items.Select(member => member.PersonId).Should().Equal(people[8], people[9]);
        var bad = () => Roles.Members(key, take: 0);
        await bad.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    /// <summary>Past its capacity the bag cache evicts one bag, chosen by the clock, and keeps the others warm.</summary>
    [Fact] public async Task The_bag_cache_past_its_capacity_evicts_one_entry_and_keeps_the_rest_warm()
    {
        var cache = new RoleBagCache(Options.Create(new RoleOptions { MaxCachedBags = 3 }));
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        string Person(int i) => $"person:cache:{suffix}:{i}";
        var warm = new List<RoleBag>();
        for (var i = 0; i < 3; i++) warm.Add(await cache.Get(Person(i), ct));
        for (var i = 0; i < 3; i++) (await cache.Get(Person(i), ct)).Should().BeSameAs(warm[i]);

        var fourth = await cache.Get(Person(3), ct);

        cache.BagCount.Should().Be(3, "admitting a fourth evicts exactly one");
        cache.HoldsBag(Person(3)).Should().BeTrue();
        Enumerable.Range(0, 3).Count(i => cache.HoldsBag(Person(i))).Should().Be(2, "one bag was evicted, the others stay");
        for (var i = 0; i < 3; i++)
        {
            if (cache.HoldsBag(Person(i))) (await cache.Get(Person(i), ct)).Should().BeSameAs(warm[i], "a bag that stayed is still warm");
        }

        (await cache.Get(Person(3), ct)).Should().BeSameAs(fourth);
    }

    [Fact] public async Task Adding_a_person_past_the_roles_one_person_holds_is_refused_with_its_correction()
    {
        var options = Options.Create(new RoleOptions { MaxRolesPerPerson = 2 });
        var roles = new RoleCollection(new RoleBagCache(options), options);
        var suffix = Guid.NewGuid().ToString("N");
        var person = $"person:busy:{suffix}";
        for (var i = 0; i < 3; i++) await roles.Define($"role:busy:{suffix}:{i}", "Busy", []);
        await roles.Add($"role:busy:{suffix}:0", person);
        await roles.Add($"role:busy:{suffix}:1", person);

        var third = () => roles.Add($"role:busy:{suffix}:2", person);
        var refused = (await third.Should().ThrowAsync<RoleBoundException>()).Which;
        refused.ConfigurationKey.Should().Be("Koan:Identity:Roles:MaxRolesPerPerson");
        refused.Message.Should().Contain("Remove one of their roles, or raise the bound");
        (await roles.Add($"role:busy:{suffix}:1", person)).PersonId.Should().Be(person, "adding a role they hold is no new role");
        (await roles.ForPerson(person)).Should().HaveCount(2);
    }

    [Fact] public async Task Deleting_a_role_removes_its_members_and_a_role_defined_again_starts_with_none()
    {
        var key = $"role:deleted:{Guid.NewGuid():N}";
        await Roles.Define(key, "Deleted", ["global:post"]);
        for (var i = 0; i < 7; i++) await Roles.Add(key, $"person:deleted:{i}");
        var bag = await Roles.Bag("person:deleted:3", true);
        bag.InRole(key).Should().BeTrue();

        (await Roles.Delete(key)).Should().BeTrue();

        (await Roles.Bag("person:deleted:3", true)).InRole(key).Should().BeFalse("the member's bag compiled again");
        (await RoleMember.Count.Where(row => row.RoleKey == key, CountStrategy.Exact)).Should().Be(0);
        await Roles.Define(key, "Defined again", ["global:post"]);
        (await Roles.Members(key)).Items.Should().BeEmpty("a role defined again under the key starts with no member");
    }

    [Fact] public async Task A_membership_row_is_written_only_by_the_role_collection()
    {
        var key = $"role:guarded-member:{Guid.NewGuid():N}";
        await Roles.Define(key, "Guarded", []);
        var direct = () => new RoleMember { Id = RoleMember.KeyFor(key, "person:direct"), RoleKey = key, PersonId = "person:direct" }.Save();
        (await direct.Should().ThrowAsync<EntityLifecycleCancelledException>()).Which.ReasonCode.Should().Be("role.mutation.guard");
        var member = await Roles.Add(key, "person:direct");
        var remove = () => member.Remove();
        await remove.Should().ThrowAsync<EntityLifecycleCancelledException>();
        (await Roles.Members(key)).Items.Should().ContainSingle();
    }

    /// <summary>
    /// A deletion that stops part way — here after its first page of members — leaves a role that grants nothing and
    /// still denies what it denied: no member still in it holds the role's permission anywhere, nor an overwrite's allow
    /// on any resource, and the role's deny still withholds what another role grants; a retry finishes the deletion.
    /// </summary>
    [Fact] public async Task A_delete_that_fails_after_the_first_member_page_leaves_no_member_holding_what_the_role_granted()
    {
        var options = Options.Create(new RoleOptions { MaxPageSize = 2 });
        var roles = new RoleCollection(new RoleBagCache(options), options, new Actor("operator:deleting"));
        var suffix = Guid.NewGuid().ToString("N");
        var key = $"role:doomed:{suffix}";
        var viewer = $"role:viewer:{suffix}";
        var resource = $"resource:doomed:{suffix}";
        var allowed = $"resource:allowed:{suffix}";
        var mixed = $"resource:mixed:{suffix}";
        const string post = "global:doomed_post";
        const string pin = "global:doomed_pin";
        const string view = "global:doomed_view";
        await roles.Define(key, "Doomed", [post]);
        await roles.Define(viewer, "Viewer", [view]);
        await roles.Overwrite(resource, OverwriteSubjectKind.Role, key, [], [post]);
        await roles.Overwrite(allowed, OverwriteSubjectKind.Role, key, [pin], []);
        await roles.Overwrite(mixed, OverwriteSubjectKind.Role, key, [pin], [view]);
        var people = Enumerable.Range(0, 5).Select(i => $"person:doomed:{suffix}:{i}").ToArray();
        foreach (var person in people)
        {
            await roles.Add(key, person);
            await roles.Add(viewer, person);
        }

        var before = await roles.Bag(people[4], true);
        before.Permissions.Should().Contain(post);
        before.On(allowed, await roles.OverwritesFor(before, allowed), DateTimeOffset.UtcNow).Holds(pin).Should().BeTrue();

        using var stop = new CancellationTokenSource();
        using (AppHost.PushScope(new WithActor(_fixture.Services, new StopAfter(stop, calls: 2))))
        {
            var delete = () => roles.Delete(key, stop.Token);
            await delete.Should().ThrowAsync<OperationCanceledException>();
        }

        var remaining = (await Roles.Members(key)).Items.Select(member => member.PersonId).ToArray();
        remaining.Should().Equal(people[2..], "the first page of members was removed, then the deletion stopped");
        foreach (var person in remaining)
        {
            var bag = await roles.Bag(person, true);
            bag.Permissions.Should().NotContain(post, "a role being deleted grants nothing").And.Contain(view);
            foreach (var place in new[] { resource, allowed, mixed })
            {
                var here = bag.On(place, await roles.OverwritesFor(bag, place), DateTimeOffset.UtcNow);
                here.Holds(post).Should().BeFalse("losing the role's overwrite never leaves its grant behind");
                here.Holds(pin).Should().BeFalse("a role being deleted allows nothing on any resource");
            }

            bag.On(mixed, await roles.OverwritesFor(bag, mixed), DateTimeOffset.UtcNow).Holds(view).Should().BeFalse(
                "the role's deny still withholds what another role grants until the member leaves it");
        }

        var left = (await Roles.OverwritesOf(OverwriteSubjectKind.Role, key)).Items;
        left.Should().OnlyContain(row => row.Allow.Count == 0, "only the role's denies remain");
        left.Select(row => row.ResourceId).Should().BeEquivalentTo([resource, mixed]);

        (await roles.Delete(key)).Should().BeTrue("a retry finishes the deletion");
        (await Roles.Members(key)).Items.Should().BeEmpty();
        (await roles.Get(key)).Should().BeNull();
    }

    /// <summary>Erasure removes every membership row of a person a page at a time, so a person left over the
    /// roles-per-person bound — which the bounded read refuses — is still erased.</summary>
    [Fact] public async Task Erasure_removes_every_membership_of_a_person_over_the_bound()
    {
        var wide = Options.Create(new RoleOptions { MaxRolesPerPerson = 5 });
        var holding = new RoleCollection(new RoleBagCache(wide), wide);
        var suffix = Guid.NewGuid().ToString("N");
        var person = $"person:over:{suffix}";
        for (var i = 0; i < 5; i++)
        {
            await holding.Define($"role:over:{suffix}:{i}", "Over", [P]);
            await holding.Add($"role:over:{suffix}:{i}", person);
        }

        var narrow = Options.Create(new RoleOptions { MaxRolesPerPerson = 2, MaxPageSize = 2 });
        var roles = new RoleCollection(new RoleBagCache(narrow), narrow);
        var bounded = () => roles.ForPerson(person);
        await bounded.Should().ThrowAsync<InvalidOperationException>("the person holds more roles than the bound");

        var erasure = new Koan.Identity.Erasure.IdentityCoreErasureContributor(roles, narrow);
        (await erasure.PreviewAsync(person)).EstimatedItems.Should().Be(5);
        var erased = await erasure.EraseAsync(person);
        erased.Counts[Koan.Identity.Infrastructure.IdentityErasureConstants.Counts.GlobalRoles].Should().Be(5);
        (await RoleMember.Count.Where(member => member.PersonId == person, CountStrategy.Exact)).Should().Be(0);
    }

    [Fact] public async Task Concurrent_joins_of_one_person_never_exceed_the_roles_one_person_holds()
    {
        var options = Options.Create(new RoleOptions { MaxRolesPerPerson = 2 });
        var roles = new RoleCollection(new RoleBagCache(options), options);
        var suffix = Guid.NewGuid().ToString("N");
        var person = $"person:racing:{suffix}";
        var keys = Enumerable.Range(0, 8).Select(i => $"role:racing:{suffix}:{i}").ToArray();
        foreach (var key in keys) await roles.Define(key, "Racing", [P]);

        var joined = await Task.WhenAll(keys.Select(key => Task.Run(async () =>
        {
            try { await roles.Add(key, person); return true; }
            catch (RoleBoundException) { return false; }
        })));

        joined.Count(join => join).Should().Be(2);
        (await roles.ForPerson(person)).Should().HaveCount(2);
    }

    private const string P = "global:post";

    private sealed class Actor(string subject) : IIdentityActorAccessor
    {
        public string? CurrentActorSubject => subject;
    }

    /// <summary>
    /// Read by the audit of each membership row removed — the role and overwrite rows here name their actor, so the
    /// audit asks nothing for them — and stops the flow once the given number of rows has gone.
    /// </summary>
    private sealed class StopAfter(CancellationTokenSource stop, int calls) : IIdentityActorAccessor
    {
        private int _seen;

        public string? CurrentActorSubject
        {
            get
            {
                if (Interlocked.Increment(ref _seen) == calls) stop.Cancel();
                return null;
            }
        }
    }

    private sealed class WithActor(IServiceProvider inner, IIdentityActorAccessor actor) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IIdentityActorAccessor) ? actor : inner.GetService(serviceType);
    }

    private sealed record TestPost(string ReadPermission);
}
