using AwesomeAssertions;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Identity.Tests;

/// <summary>
/// SEC-0031: every role stands in one dense order, <c>everyone</c> at 0, and a bag manages the roles below its highest.
/// The store is shared with the other Identity specs, so each spec places its own roles and reads the order relative to
/// them, and checks that the whole order is dense.
/// </summary>
[Collection("identity")]
public sealed class RolePositionSpec : IdentityHostScopedSpec
{
    private readonly IdentityHostFixture _fixture;
    public RolePositionSpec(IdentityHostFixture fixture) : base(fixture) => _fixture = fixture;
    private RoleCollection Roles => _fixture.Services.GetRequiredService<RoleCollection>();

    [Fact] public async Task A_new_role_is_placed_at_the_top_or_at_the_position_asked_and_the_roles_above_move_up()
    {
        var keys = Keys("placed", 2);
        var (a, b) = (await Roles.Define(keys[0], "A"), await Roles.Define(keys[1], "B"));
        b.Position.Should().Be(a.Position + 1, "a role defined without a position is placed at the top");
        (await Top()).Should().Be(b.Position);

        var between = await Roles.Define(Keys("between", 1)[0], "Between", position: a.Position + 1);
        between.Position.Should().Be(a.Position + 1);
        (await Position(keys[0])).Should().Be(a.Position);
        (await Position(keys[1])).Should().Be(a.Position + 2, "the role at the position asked, and every role above it, moved up one");

        var bottom = await Roles.Define(Keys("bottom", 1)[0], "Bottom", position: -5);
        bottom.Position.Should().Be(1, "a position below 1 places the role at the bottom, above everyone");
        (await Position(keys[0])).Should().Be(a.Position + 1);
        var top = await Roles.Define(Keys("top", 1)[0], "Top", position: int.MaxValue);
        top.Position.Should().Be(await Top(), "a position past the top places the role at the top");

        var renamed = await Roles.Define(keys[0], "A again");
        renamed.Position.Should().Be(a.Position + 1, "defining a role that exists, without a position, leaves it where it is");
        await ExpectDense();
    }

    [Fact] public async Task A_role_moves_up_and_down_and_the_roles_it_passes_close_ranks()
    {
        var keys = Keys("move", 4);
        foreach (var key in keys) await Roles.Define(key, key);
        var low = await Position(keys[0]);

        (await Roles.Move(keys[0], low + 3)).Position.Should().Be(low + 3);
        (await Positions(keys)).Should().Equal(low + 3, low, low + 1, low + 2);

        (await Roles.Move(keys[0], low)).Position.Should().Be(low);
        (await Positions(keys)).Should().Equal(low, low + 1, low + 2, low + 3);

        (await Roles.Move(keys[3], low + 1)).Position.Should().Be(low + 1);
        (await Positions(keys)).Should().Equal(low, low + 2, low + 3, low + 1);

        (await Roles.Move(keys[1], int.MaxValue)).Position.Should().Be(await Top(), "a position past the top moves the role to the top");
        await ExpectDense();

        var everyone = () => Roles.Move(RoleTokens.Everyone, 2);
        await everyone.Should().ThrowAsync<ArgumentException>("everyone is always at 0");
        var missing = () => Roles.Move(Keys("missing", 1)[0], 1);
        await missing.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact] public async Task Concurrent_moves_and_changes_keep_one_dense_order_and_lose_no_change()
    {
        var keys = Keys("concurrent", 8);
        foreach (var key in keys) await Roles.Define(key, key, ["global:before"]);
        var low = await Position(keys[0]);
        var random = new Random(27);
        var moves = Enumerable.Range(0, 48)
            .Select(_ => (Key: keys[random.Next(keys.Length)], To: low + random.Next(keys.Length)))
            .Select(move => (Func<Task>)(() => Roles.Move(move.Key, move.To)));
        var changes = keys.Select((key, i) => (Func<Task>)(() => i % 2 == 0
            ? Roles.Rename(key, $"renamed {i}")
            : Roles.SetPermissions(key, ["global:after"])));

        await Task.WhenAll(moves.Concat(changes).Select(work => Task.Run(work)));

        (await Positions(keys)).Order().Should().Equal(Enumerable.Range(low, keys.Length), "the moved roles still hold exactly the positions they held between them");
        await ExpectDense();
        for (var i = 0; i < keys.Length; i++)
        {
            var role = (await Roles.Get(keys[i]))!;
            if (i % 2 == 0) role.Name.Should().Be($"renamed {i}", "a move never writes back a name it read before the rename");
            else role.Permissions.Should().Equal(["global:after"], "a move never writes back permissions it read before the change");
        }
    }

    [Fact] public async Task Deleting_a_role_closes_its_gap_and_its_neighbours_bags_compile_again()
    {
        var keys = Keys("gap", 3);
        foreach (var key in keys) await Roles.Define(key, key, ["global:gap"]);
        var person = $"person:gap:{Guid.NewGuid():N}";
        await Roles.Add(keys[2], person);
        var low = await Position(keys[0]);
        (await Roles.Bag(person, true)).Highest.Should().Be(low + 2);

        (await Roles.Delete(keys[1])).Should().BeTrue();

        (await Position(keys[2])).Should().Be(low + 1, "the role above the deleted one moved down into its place");
        (await Roles.Bag(person, true)).Highest.Should().Be(low + 1, "a bag holds the position of its highest role, compiled again");
        await ExpectDense();
    }

    [Fact] public async Task A_new_authenticated_role_is_placed_at_the_bottom_since_every_signed_in_person_holds_it()
    {
        try
        {
            (await Roles.Define(RoleTokens.Authenticated, "Signed in")).Position.Should().Be(1);
            (await Roles.Bag($"person:signed-in:{Guid.NewGuid():N}", true)).Highest.Should().Be(1);
            (await Roles.Bag(null, false)).Highest.Should().Be(0, "a visitor holds only everyone");
            await ExpectDense();
        }
        finally { await Roles.Delete(RoleTokens.Authenticated); }
        await ExpectDense();
    }

    /// <summary>
    /// A member, a moderator, an administrator and a visitor, each judged by the one rule: an actor manages a role below
    /// their highest, and its members, and moves it only below their highest; a role granting the administrator token
    /// manages every role; nobody manages their own highest role or moves everyone.
    /// </summary>
    [Fact] public async Task Each_actor_manages_the_roles_below_their_highest_an_administrator_every_role_and_nobody_their_own_highest()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var member = await Roles.Define($"role:member:{suffix}", "Member", ["global:post"]);
        var moderator = await Roles.Define($"role:moderator:{suffix}", "Moderator", ["global:post", "global:manage_roles"]);
        var administrator = await Roles.Define($"role:administrator:{suffix}", "Administrator", [RoleTokens.Administrator]);
        var crown = await Roles.Define($"role:crown:{suffix}", "Crown");
        var (memberId, moderatorId, administratorId) = ($"person:member:{suffix}", $"person:moderator:{suffix}", $"person:administrator:{suffix}");
        await Roles.Add(member.Key, memberId);
        await Roles.Add(member.Key, moderatorId);
        await Roles.Add(moderator.Key, moderatorId);
        await Roles.Add(administrator.Key, administratorId);

        var asMember = await Roles.Bag(memberId, true);
        asMember.Highest.Should().Be(member.Position);
        (await Roles.Manages(asMember, member.Key)).Should().BeFalse("nobody manages their own highest role");
        (await Roles.Manages(asMember, moderator.Key)).Should().BeFalse();
        (await Roles.Manages(asMember, RoleTokens.Everyone)).Should().BeTrue("everyone is below every role");
        (await Roles.Manages(asMember, RoleTokens.Everyone, to: 1)).Should().BeFalse("nobody moves everyone");

        var asModerator = await Roles.Bag(moderatorId, true);
        asModerator.Highest.Should().Be(moderator.Position, "a bag stands at its highest role, not its first");
        (await Roles.Manages(asModerator, member.Key)).Should().BeTrue("a moderator manages the member role and its members");
        asModerator.Manages(member).Should().BeTrue("the pure rule answers over a row the caller has read");
        (await Roles.Manages(asModerator, member.Key, to: moderator.Position - 1)).Should().BeTrue("it stays below them");
        (await Roles.Manages(asModerator, member.Key, to: moderator.Position)).Should().BeFalse("it would stand above them");
        (await Roles.Manages(asModerator, moderator.Key)).Should().BeFalse("nobody manages their own highest role");
        (await Roles.Manages(asModerator, administrator.Key)).Should().BeFalse();
        (await Roles.Manages(asModerator, crown.Key)).Should().BeFalse();

        var created = $"role:helper:{suffix}";
        (await Roles.Manages(asModerator, created)).Should().BeFalse("a role defined without a position is placed at the top");
        (await Roles.Manages(asModerator, created, to: asModerator.Highest)).Should().BeTrue("placed there, it is directly below them");
        (await Roles.Manages(asModerator, created, to: asModerator.Highest + 1)).Should().BeFalse();
        var helper = await Roles.Define(created, "Helper", position: asModerator.Highest);
        helper.Position.Should().Be(moderator.Position, "it takes the moderator's place, directly below them");
        asModerator = await Roles.Bag(moderatorId, true);
        asModerator.Highest.Should().Be(moderator.Position + 1, "the moderator role moved up one, and the bag compiled again");
        (await Roles.Manages(asModerator, created)).Should().BeTrue("the creator manages the role placed below them");

        var asAdministrator = await Roles.Bag(administratorId, true);
        (await Roles.Manages(asAdministrator, moderator.Key)).Should().BeTrue();
        (await Roles.Manages(asAdministrator, member.Key, to: int.MaxValue)).Should().BeTrue("an administrator moves a role anywhere");
        (await Roles.Manages(asAdministrator, crown.Key)).Should().BeTrue("the administrator token manages every role, even one above");
        (await Roles.Manages(asAdministrator, administrator.Key)).Should().BeFalse("nobody manages their own highest role");
        (await Roles.Manages(asAdministrator, Keys("unplaced", 1)[0])).Should().BeTrue("an administrator manages a role placed at the top");

        var visitor = await Roles.Bag(null, false);
        visitor.Highest.Should().Be(0);
        (await Roles.Manages(visitor, member.Key)).Should().BeFalse("a visitor holds only everyone, the bottom");
        (await Roles.Manages(visitor, RoleTokens.Everyone)).Should().BeFalse("everyone is the visitor's own highest role");
    }

    private static string[] Keys(string name, int count)
    {
        var suffix = Guid.NewGuid().ToString("N");
        return [.. Enumerable.Range(0, count).Select(i => $"role:{name}:{suffix}:{i}")];
    }

    private async Task<int> Position(string key) => (await Roles.Get(key))!.Position;

    private async Task<int[]> Positions(IEnumerable<string> keys)
    {
        var positions = new List<int>();
        foreach (var key in keys) positions.Add(await Position(key));
        return [.. positions];
    }

    private async Task<int> Top() => (await Roles.Page(pageSize: 1)).Items.Single().Position;

    /// <summary>Every role in the order, the highest first, read a page at a time.</summary>
    private async Task<IReadOnlyList<Role>> Order()
    {
        var roles = new List<Role>();
        for (var page = 1; ; page++)
        {
            var read = await Roles.Page(page: page, pageSize: 100);
            roles.AddRange(read.Items);
            if (read.Items.Count < 100) return roles;
        }
    }

    /// <summary>The roles other than everyone hold 1 to their number, each once, read highest first; everyone holds 0.</summary>
    private async Task ExpectDense()
    {
        var order = await Order();
        var rest = order.Where(role => role.Key != RoleTokens.Everyone).ToArray();
        rest.Select(role => role.Position).Should().Equal(Enumerable.Range(1, rest.Length).Reverse(), "the order is dense and read highest first");
        order.Where(role => role.Key == RoleTokens.Everyone).Should().AllSatisfy(role => role.Position.Should().Be(0));
    }
}
