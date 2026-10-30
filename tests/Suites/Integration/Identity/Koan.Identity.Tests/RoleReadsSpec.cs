using AwesomeAssertions;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Identity.Tests;

/// <summary>
/// What the role model reads is what it was asked for, exactly: a store whose collation equates keys never lends one
/// key's rows to another, cursors continue from the row the store returned, and no key can be confused with another
/// inside a composed id.
/// </summary>
[Collection("identity")]
public sealed class RoleReadsSpec : IdentityHostScopedSpec
{
    private const string P = "global:post";
    private readonly IdentityHostFixture _fixture;
    public RoleReadsSpec(IdentityHostFixture fixture) : base(fixture) => _fixture = fixture;
    private RoleCollection Roles => _fixture.Services.GetRequiredService<RoleCollection>();

    /// <summary>
    /// A store that ignores case answers a read for <c>doc</c> with <c>Doc</c>'s rows too. The page keeps only the rows
    /// whose keys equal the asked ones ordinally, and its cursor is the last row the store returned — its own key and
    /// id — so the next page continues past every row the store matched.
    /// </summary>
    [Fact]
    public void A_page_keeps_only_exact_rows_and_continues_from_the_last_row_the_store_returned()
    {
        var own = Row("doc", "role:a");
        var variant = Row("Doc", "role:b");
        var later = Row("doc", "role:c");
        static bool Exact(PermissionOverwrite row) => StringComparer.Ordinal.Equals(row.ResourceId, "doc");

        var (rows, next) = RoleReads.Keep<PermissionOverwrite>([own, variant, later], Exact, row => row.SubjectId, take: 2);
        rows.Should().Equal([own], "the row of another resource that the store's collation matched is not this resource's");
        RoleReads.ParseCursor(next).Should().Be(("role:b", variant.Id), "the cursor is the last row the store returned, by its own key and id");

        var (last, end) = RoleReads.Keep<PermissionOverwrite>([variant, later], Exact, row => row.SubjectId, take: 2);
        last.Should().Equal([later]);
        end.Should().BeNull();

        var (none, onward) = RoleReads.Keep<PermissionOverwrite>([variant, later], Exact, row => row.SubjectId, take: 1);
        none.Should().BeEmpty("a page can hold no row of the scope while more follow");
        onward.Should().NotBeNull();

        var invented = () => RoleReads.ParseCursor("no-separator");
        invented.Should().Throw<ArgumentException>();
        var controlled = () => RoleReads.ParseCursor("ID:key\u001fmore");
        controlled.Should().Throw<ArgumentException>();
    }

    /// <summary>Keys that differ only in case are different keys: each role lists only its own members, each resource
    /// evaluates only its own overwrites, and each listing pages through only its own rows.</summary>
    [Fact]
    public async Task Keys_that_differ_only_in_case_never_share_rows()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var lower = $"role:case:{suffix}";
        var upper = $"role:CASE:{suffix}";
        await Roles.Define(lower, "lower", [P]);
        await Roles.Define(upper, "upper", []);
        foreach (var i in Enumerable.Range(0, 3))
        {
            await Roles.Add(lower, $"person:lower:{suffix}:{i}");
            await Roles.Add(upper, $"person:upper:{suffix}:{i}");
        }

        (await All(after => Roles.Members(lower, after, take: 1))).Should().Equal(Enumerable.Range(0, 3).Select(i => $"person:lower:{suffix}:{i}"));
        (await All(after => Roles.Members(upper, after, take: 1))).Should().Equal(Enumerable.Range(0, 3).Select(i => $"person:upper:{suffix}:{i}"));

        var doc = $"doc:{suffix}";
        var Doc = $"Doc:{suffix}";
        var person = $"person:lower:{suffix}:0";
        await Roles.Overwrite(doc, OverwriteSubjectKind.Role, lower, [], [P]);
        await Roles.Overwrite(Doc, OverwriteSubjectKind.Role, lower, ["global:pin"], []);
        await Roles.Overwrite(Doc, OverwriteSubjectKind.Person, person, [], ["global:view"]);
        var bag = await Roles.Bag(person, true);

        var onDoc = bag.On(doc, await Roles.OverwritesFor(bag, doc), DateTimeOffset.UtcNow);
        onDoc.Holds(P).Should().BeFalse("doc's own deny applies on doc");
        onDoc.Holds("global:pin").Should().BeFalse("Doc's allow is not doc's");
        var onUpper = bag.On(Doc, await Roles.OverwritesFor(bag, Doc), DateTimeOffset.UtcNow);
        onUpper.Holds(P).Should().BeTrue("doc's deny is not Doc's");
        onUpper.Holds("global:pin").Should().BeTrue();
        (await Roles.OverwritesFor(bag, doc)).Should().OnlyContain(row => row.ResourceId == doc);
        (await Roles.Overwrites(Doc, take: 1)).Items.Should().OnlyContain(row => row.ResourceId == Doc);
        (await Roles.OverwritesOf(OverwriteSubjectKind.Role, lower, take: 100)).Items.Select(row => row.ResourceId)
            .Should().Contain([doc, Doc]).And.OnlyHaveUniqueItems();
    }

    /// <summary>A key holding a control character is refused, so no two keys compose one id; and an id composed from
    /// parts names those parts alone, whatever text they hold.</summary>
    [Fact]
    public async Task A_key_with_a_control_character_is_refused_and_composed_ids_never_collide()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var key = $"role:control:{suffix}";
        await Roles.Define(key, "Control", [P]);

        var role = () => Roles.Define($"role:a\u001fx:{suffix}", "Bad", []);
        (await role.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("control character");
        var person = () => Roles.Add(key, "x\u001fperson:c");
        await person.Should().ThrowAsync<ArgumentException>();
        var resource = () => Roles.Overwrite($"doc\u0000{suffix}", OverwriteSubjectKind.Role, key, [], [P]);
        await resource.Should().ThrowAsync<ArgumentException>();
        var subject = () => Roles.Overwrite($"doc:{suffix}", OverwriteSubjectKind.Person, "person\u001f", [], [P]);
        await subject.Should().ThrowAsync<ArgumentException>();
        var bag = () => Roles.Bag("person\u0007bell", true);
        await bag.Should().ThrowAsync<ArgumentException>();
        new RoleBag("person:ok", true, [key], [P]).InRole($"{key}\u001f").Should().BeFalse();
        (await Roles.Define(key, "Line one\nline two", [P])).Name.Should().Contain("\n", "a role's name is text, not a key");

        RoleMember.KeyFor("role:a\u001fx", "person:c").Should().NotBe(RoleMember.KeyFor("role:a", "x\u001fperson:c"));
        PermissionOverwrite.KeyFor("doc\u001fRole", OverwriteSubjectKind.Person, "x")
            .Should().NotBe(PermissionOverwrite.KeyFor("doc", OverwriteSubjectKind.Role, "Person\u001fx"));
    }

    /// <summary>
    /// A store that ignores case compares ids that way too, so two keys differing only in case would share a row if a
    /// row's id were its key. Each role's id is composed from its key — upper-case hex, distinct even when case is
    /// ignored — and every read of a role by key reads that id, so <c>Admin</c> and <c>admin</c> stay two roles.
    /// </summary>
    [Fact]
    public async Task Role_keys_that_differ_only_in_case_compose_different_ids_and_stay_separate()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var upper = $"Admin:{suffix}";
        var lower = $"admin:{suffix}";
        string.Equals(Role.KeyFor(upper), Role.KeyFor(lower), StringComparison.OrdinalIgnoreCase)
            .Should().BeFalse("an id that a case-ignoring store compares stays distinct");
        string.Equals(RoleMember.KeyFor(upper, "person:x"), RoleMember.KeyFor(lower, "person:x"), StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        string.Equals(PermissionOverwrite.KeyFor("Doc", OverwriteSubjectKind.Role, lower), PermissionOverwrite.KeyFor("doc", OverwriteSubjectKind.Role, lower),
            StringComparison.OrdinalIgnoreCase).Should().BeFalse();

        await Roles.Define(lower, "lower", []);
        await Roles.Define(upper, "Upper", [P]);
        var person = $"person:case:{suffix}";
        await Roles.Add(lower, person);

        var stored = await Roles.Get(lower);
        stored!.Key.Should().Be(lower);
        stored.Id.Should().Be(Role.KeyFor(lower));
        stored.Name.Should().Be("lower", "defining Admin left admin as it was");
        stored.Permissions.Should().BeEmpty();
        (await Roles.Get(upper))!.Permissions.Should().Equal(P);
        (await Roles.ForPerson(person)).Select(role => role.Key).Should().Equal(lower);
        var bag = await Roles.Bag(person, true);
        bag.InRole(lower).Should().BeTrue();
        bag.InRole(upper).Should().BeFalse();
        bag.Permissions.Should().NotContain(P);
        (await Roles.Members(upper)).Items.Should().BeEmpty();
    }

    /// <summary>A key is well-formed UTF-16: a lone surrogate is refused, since encoding would turn it into the
    /// replacement character and compose the id of another key; a surrogate pair is a character like any other.</summary>
    [Fact]
    public async Task A_key_with_an_unpaired_surrogate_is_refused_and_an_id_is_never_hashed_from_one()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var key = $"role:surrogate:{suffix}";
        await Roles.Define(key, "Surrogate", [P]);

        var high = () => Roles.Add(key, $"person:{suffix}:\uD800");
        (await high.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("unpaired surrogate");
        var low = () => Roles.Overwrite($"doc:{suffix}", OverwriteSubjectKind.Person, $"person:\uDC00{suffix}", [], [P]);
        await low.Should().ThrowAsync<ArgumentException>();
        var reversed = () => Roles.Bag($"person:\uDE00\uD83D:{suffix}", true);
        await reversed.Should().ThrowAsync<ArgumentException>();
        (await Roles.Add(key, $"person:{suffix}:\uD83D\uDE00")).PersonId.Should().EndWith("\uD83D\uDE00", "a pair is a character");
        (await Roles.Add(key, $"person:{suffix}:\uFFFD")).Id.Should().NotBe(RoleMember.KeyFor(key, $"person:{suffix}:\uD83D\uDE00"));

        var composed = () => RoleMember.KeyFor(key, $"person:{suffix}:\uD800");
        composed.Should().Throw<ArgumentException>("an id is never hashed from a string that is not well-formed");
        var overwrite = () => PermissionOverwrite.KeyFor($"doc:\uDC00", OverwriteSubjectKind.Role, key);
        overwrite.Should().Throw<ArgumentException>();
    }

    private static async Task<IReadOnlyList<string>> All(Func<string?, Task<RoleMemberPage>> page)
    {
        var people = new List<string>();
        for (string? after = null; ;)
        {
            var current = await page(after);
            people.AddRange(current.Items.Select(member => member.PersonId));
            if ((after = current.Next) is null) return people;
        }
    }

    private static PermissionOverwrite Row(string resource, string subject) => new()
    {
        Id = PermissionOverwrite.KeyFor(resource, OverwriteSubjectKind.Role, subject),
        ResourceId = resource,
        SubjectKind = OverwriteSubjectKind.Role,
        SubjectId = subject,
        Deny = [P],
    };
}
