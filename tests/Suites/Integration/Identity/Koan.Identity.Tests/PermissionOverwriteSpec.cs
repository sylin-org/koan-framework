using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Data.Core.Lifecycle;
using Koan.Identity.Access;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Koan.Identity.Tests;

/// <summary>
/// Per-resource overwrites resolved in Discord's documented order
/// (https://discord.com/developers/docs/topics/permissions#permission-overwrites): the resolution over rows the caller
/// loaded, the deciding fact, the contributor and its revoke, the bounds, the cache, the events and the guard.
/// </summary>
[Collection("identity")]
public sealed class PermissionOverwriteSpec : IdentityHostScopedSpec
{
    private const string P = "global:post";
    private const string View = "global:view";
    private const string Resource = "resource:workshop";
    private const string Person = "person:discord";
    private const string HeldRole = "role:held";
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityHostFixture _fixture;
    public PermissionOverwriteSpec(IdentityHostFixture fixture) : base(fixture) => _fixture = fixture;
    private RoleCollection Roles => _fixture.Services.GetRequiredService<RoleCollection>();

    /// <summary>Discord's documented cases: base, everyone overwrite, overwrite of a role the person holds, the
    /// person's own overwrite, and whether P is held; for a missing P, the subject of the deciding row.</summary>
    public static TheoryData<string> DiscordCases => new(Cases.Keys);

    private static readonly Dictionary<string, Case> Cases = new()
    {
        ["allow P | - | - | - => P"] = new([P], null, null, null, true, null),
        ["allow P | deny P | - | - => no P"] = new([P], Deny(), null, null, false, RoleTokens.Everyone),
        ["allow P | deny P | allow P | - => P"] = new([P], Deny(), Allow(), null, true, null),
        ["allow P | - | deny P | allow P => P"] = new([P], null, Deny(), Allow(), true, null),
        ["allow P | - | allow P | deny P => no P"] = new([P], null, Allow(), Deny(), false, Person),
        ["- | allow P | - | - => P"] = new([], Allow(), null, null, true, null),
        ["administrator | deny P | deny P | deny P => P"] = new([RoleTokens.Administrator], Deny(), Deny(), Deny(), true, null),
    };

    [Theory, MemberData(nameof(DiscordCases))]
    public void Discord_documented_case_resolves_and_names_the_deciding_row(string name)
    {
        var @case = Cases[name];
        var rows = new List<PermissionOverwrite>();
        if (@case.Everyone is { } everyone) rows.Add(Row(OverwriteSubjectKind.Role, RoleTokens.Everyone, everyone));
        if (@case.HeldRole is { } role) rows.Add(Row(OverwriteSubjectKind.Role, HeldRole, role));
        if (@case.Member is { } member) rows.Add(Row(OverwriteSubjectKind.Person, Person, member));

        var here = Bag(@case.Base).On(Resource, rows, Now);

        here.Holds(P).Should().Be(@case.Holds, name);
        var decision = here.Explain(P);
        decision.Allowed.Should().Be(@case.Holds);
        if (@case.Holds) return;
        var deciding = decision.ContributingFacts.Should().ContainSingle().Subject;
        deciding.Kind.Should().Be(AccessFactKind.Deny);
        deciding.Value.Should().Be(P);
        deciding.Resource.Should().Be(Resource);
        deciding.Subject.Should().Be(@case.DecidingSubject);
        deciding.RowType.Should().Be(nameof(PermissionOverwrite));
        deciding.RowId.Should().Be(rows.Single(row => row.SubjectId == @case.DecidingSubject).Id);
        decision.Reason.Should().Contain(P).And.Contain(Resource);
    }

    [Fact]
    public void Administrator_holds_every_permission_and_no_overwrite_applies()
    {
        var here = Bag([RoleTokens.Administrator]).On(Resource,
            [Row(OverwriteSubjectKind.Role, RoleTokens.Everyone, Deny(View))], Now);
        here.IsAdministrator.Should().BeTrue();
        here.Holds(View).Should().BeTrue();
        here.Holds("global:anything_at_all").Should().BeTrue();
    }

    [Fact]
    public void A_permission_nothing_grants_is_explained_by_the_ungranted_fact()
    {
        var decision = Bag([View]).On(Resource, [], Now).Explain(P);
        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be($"no held role grants {P}");
        var fact = decision.ContributingFacts.Should().ContainSingle().Subject;
        fact.Kind.Should().Be(AccessFactKind.Ungranted);
        fact.Subject.Should().Be(Person);
        fact.RowId.Should().BeEmpty("no row backs it, so nothing can revoke it");
    }

    [Fact]
    public void A_role_the_person_does_not_hold_and_another_person_contribute_nothing()
    {
        var here = Bag([P]).On(Resource,
        [
            Row(OverwriteSubjectKind.Role, "role:not-held", Deny()),
            Row(OverwriteSubjectKind.Person, "person:someone-else", Deny()),
            Row(OverwriteSubjectKind.Person, HeldRole, Deny()),
        ], Now);
        here.Holds(P).Should().BeTrue();
    }

    [Fact]
    public void An_expired_overwrite_contributes_nothing()
    {
        Bag([P]).On(Resource, [Row(OverwriteSubjectKind.Role, RoleTokens.Everyone, Deny(), Now)], Now)
            .Holds(P).Should().BeTrue("a deny that expired at this instant no longer applies");
        Bag([]).On(Resource, [Row(OverwriteSubjectKind.Role, RoleTokens.Everyone, Allow(), Now.AddSeconds(-1))], Now)
            .Holds(P).Should().BeFalse("an expired allow grants nothing");
        Bag([]).On(Resource, [Row(OverwriteSubjectKind.Role, RoleTokens.Everyone, Allow(), Now.AddSeconds(1))], Now)
            .Holds(P).Should().BeTrue("an allow that has not expired applies");
    }

    [Fact]
    public void Role_step_unions_its_denies_before_its_allows_and_names_a_deterministic_row()
    {
        var bag = new RoleBag(Person, true, ["role:a", "role:b"], [P]);
        bag.On(Resource, [Row(OverwriteSubjectKind.Role, "role:a", Deny()), Row(OverwriteSubjectKind.Role, "role:b", Allow())], Now)
            .Holds(P).Should().BeTrue("one held role's allow outweighs another's deny in the same step");

        var denied = bag.On(Resource, [Row(OverwriteSubjectKind.Role, "role:b", Deny()), Row(OverwriteSubjectKind.Role, "role:a", Deny())], Now);
        denied.Explain(P).ContributingFacts.Single().Subject.Should().Be("role:a");
    }

    [Fact]
    public void Rows_for_another_resource_contribute_nothing_and_an_overwrite_never_touches_administrator()
    {
        var other = Row(OverwriteSubjectKind.Role, RoleTokens.Everyone, Deny());
        other.ResourceId = "resource:elsewhere";
        Bag([P]).On(Resource, [other], Now).Holds(P).Should().BeTrue();

        var crafted = Row(OverwriteSubjectKind.Role, RoleTokens.Everyone, new([RoleTokens.Administrator], []));
        Bag([]).On(Resource, [crafted], Now).IsAdministrator.Should().BeFalse();
    }

    [Fact]
    public void A_row_whose_role_key_cannot_name_a_role_contributes_nothing()
    {
        var bag = Bag([P]);
        var blank = Row(OverwriteSubjectKind.Role, "", Deny());
        var overlong = Row(OverwriteSubjectKind.Role, new string('r', 300), Deny());
        var padded = Row(OverwriteSubjectKind.Role, "", Deny());
        padded.SubjectId = $"  {HeldRole} ";

        bag.On(Resource, [blank, overlong], Now).Holds(P).Should().BeTrue("a row for a role the bag is not in contributes nothing");
        bag.InRole("").Should().BeFalse();
        bag.InRole(null).Should().BeFalse();
        bag.InRole(new string('r', 300)).Should().BeFalse();
        bag.InRole($" {HeldRole} ").Should().BeTrue();
        bag.On(Resource, [padded], Now).Holds(P).Should().BeFalse("a padded key that names a held role is that role");
    }

    [Fact]
    public async Task Only_a_bound_answers_409_and_a_handler_failure_keeps_its_own_exception()
    {
        var options = Options.Create(new RoleOptions { MaxRolesPerPerson = 1 });
        var bounded = new RoleCollection(new RoleBagCache(options), options);
        var suffix = Guid.NewGuid().ToString("N");
        var key = $"role:small:{suffix}";
        var other = $"role:other:{suffix}";
        await bounded.Define(key, "Small", [P]);
        await bounded.Define(other, "Other", [P]);
        var roleRoutes = Operator(new Koan.Identity.Web.RoleManagementController(bounded, options));
        var deny = new Koan.Identity.Web.PermissionOverwriteController.SaveOverwriteRequest(null, [P]);

        Koan.Data.Core.Model.Entity.Role.Reset()
            .MemberAdding(_ => throw new InvalidOperationException("store unavailable"))
            .OverwriteChanging(_ => throw new InvalidOperationException("store unavailable"));
        try
        {
            var open = Operator(new Koan.Identity.Web.RoleManagementController(Roles, _fixture.Services.GetRequiredService<IOptions<RoleOptions>>()));
            await Roles.Define($"role:open:{suffix}", "Open", [P]);
            var add = () => open.AddMember($"role:open:{suffix}", $"person:open:{suffix}", default);
            await add.Should().ThrowAsync<InvalidOperationException>().WithMessage("store unavailable");
            var put = () => Operator(new Koan.Identity.Web.PermissionOverwriteController(Roles))
                .Put(OverwriteSubjectKind.Role, $"role:open:{suffix}", $"resource:open:{suffix}", deny, default);
            await put.Should().ThrowAsync<InvalidOperationException>().WithMessage("store unavailable");
        }
        finally { Koan.Data.Core.Model.Entity.Role.Reset(); }

        (await roleRoutes.AddMember(key, $"person:one:{suffix}", default)).Result.Should().BeOfType<Microsoft.AspNetCore.Mvc.OkObjectResult>();
        var roleBound = (await roleRoutes.AddMember(other, $"person:one:{suffix}", default)).Result
            .Should().BeOfType<Microsoft.AspNetCore.Mvc.ConflictObjectResult>().Subject;
        roleBound.Value!.ToString().Should().Contain("MaxRolesPerPerson").And.Contain("Remove one of their roles, or raise the bound");
        var bound = () => bounded.Add(other, $"person:one:{suffix}");
        (await bound.Should().ThrowAsync<RoleBoundException>()).Which.ConfigurationKey.Should().Be("Koan:Identity:Roles:MaxRolesPerPerson");
    }

    private static T Operator<T>(T controller) where T : Microsoft.AspNetCore.Mvc.ControllerBase
    {
        controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                    [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "operator"),
                     new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, IdentityRoles.Operator)], "test")),
            },
        };
        return controller;
    }

    [Fact]
    public async Task Explainer_names_the_deciding_overwrite_and_revoke_through_its_ref_removes_it()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var guests = $"role:guests:{suffix}";
        var person = $"person:guest:{suffix}";
        var resource = $"resource:workshop:{suffix}";
        await Roles.Define(guests, "Guests", [View, P]);
        await Roles.Add(guests, person);
        await Roles.Overwrite(resource, OverwriteSubjectKind.Role, guests, [], [P]);

        using var scope = _fixture.Services.CreateScope();
        var explainer = scope.ServiceProvider.GetRequiredService<AccessExplainer>();
        var decision = await explainer.Explain(person, resource, P);

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be($"role '{guests}' is denied {P} on '{resource}'");
        var fact = decision.ContributingFacts.Should().ContainSingle().Subject;
        fact.Should().BeEquivalentTo(new AccessFact(nameof(PermissionOverwrite), AccessFactKind.Deny, P, resource, AccessFactScope.Global,
            nameof(PermissionOverwrite), PermissionOverwrite.KeyFor(resource, OverwriteSubjectKind.Role, guests), null, guests));
        (await explainer.Explain(person, resource, View)).Allowed.Should().BeTrue();

        (await explainer.RevokeAsync(new AccessFactRef(fact.RowType, fact.RowId, fact.Subject))).Should().BeFalse(
            "a handle that does not name the permission revokes nothing");
        (await explainer.RevokeAsync(fact.Ref)).Should().BeTrue();
        (await Roles.Overwrites(resource)).Items.Should().BeEmpty("the overwrite named nothing else, so revoking its one deny removed it");
        (await explainer.Explain(person, resource, P)).Allowed.Should().BeTrue();
        (await explainer.RevokeAsync(fact.Ref)).Should().BeFalse("nothing is left to revoke");
    }

    [Fact]
    public async Task Revoking_one_fact_of_an_overwrite_removes_only_that_fact_and_never_lifts_a_deny()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var guests = $"role:guests:{suffix}";
        var person = $"person:revoke:{suffix}";
        var board = $"board:1:{suffix}";
        const string pin = "global:pin";
        const string topic = "global:topic";
        await Roles.Define(guests, "Guests", [View, P, topic]);
        await Roles.Add(guests, person);
        await Roles.Overwrite(board, OverwriteSubjectKind.Role, guests, [pin], [P, topic]);

        using var scope = _fixture.Services.CreateScope();
        var explainer = scope.ServiceProvider.GetRequiredService<AccessExplainer>();
        var allowPin = (await explainer.Explain(person, board, pin)).ContributingFacts.Single();
        allowPin.Kind.Should().Be(AccessFactKind.Permission);

        (await explainer.RevokeAsync(allowPin.Ref)).Should().BeTrue();
        (await explainer.Explain(person, board, P)).Allowed.Should().BeFalse("revoking the allow of global:pin must not lift the row's denies");
        (await explainer.Explain(person, board, topic)).Allowed.Should().BeFalse();
        (await explainer.Explain(person, board, pin)).Allowed.Should().BeFalse("the revoked allow is gone");
        var row = (await Roles.Overwrites(board)).Items.Should().ContainSingle().Subject;
        row.Allow.Should().BeEmpty();
        row.Deny.Should().Equal(P, topic);

        var denyPost = (await explainer.Explain(person, board, P)).ContributingFacts.Single();
        (await explainer.RevokeAsync(denyPost.Ref)).Should().BeTrue();
        (await explainer.Explain(person, board, P)).Allowed.Should().BeTrue("the revoked deny is lifted");
        (await explainer.Explain(person, board, topic)).Allowed.Should().BeFalse("only that deny was removed");
        (await Roles.Overwrites(board)).Items.Single().Deny.Should().Equal(topic);

        var notPermission = new AccessFactRef(nameof(PermissionOverwrite), denyPost.RowId, null, AccessFactKind.Deny, "topic");
        (await explainer.RevokeAsync(notPermission)).Should().BeFalse("a value that is not a permission revokes nothing");
        (await explainer.RevokeAsync(new AccessFactRef(nameof(PermissionOverwrite), new string('x', 300), null, AccessFactKind.Deny, topic)))
            .Should().BeFalse("an id that cannot name a row revokes nothing");
        (await Roles.Overwrites(board)).Items.Single().Deny.Should().Equal([topic], "the row is unchanged");

        var denyTopic = (await explainer.Explain(person, board, topic)).ContributingFacts.Single();
        (await explainer.RevokeAsync(denyTopic.Ref)).Should().BeTrue();
        (await Roles.Overwrites(board)).Items.Should().BeEmpty("the row is removed once both lists are empty");
    }

    [Fact]
    public async Task A_padded_permission_names_the_same_deciding_fact()
    {
        var denied = Bag([P]).On(Resource, [Row(OverwriteSubjectKind.Role, HeldRole, Deny())], Now);
        denied.Holds("  global:post ").Should().BeFalse();
        var fact = denied.Explain(" global:post").ContributingFacts.Should().ContainSingle().Subject;
        fact.Kind.Should().Be(AccessFactKind.Deny);
        fact.Value.Should().Be(P);
        fact.Subject.Should().Be(HeldRole);

        var suffix = Guid.NewGuid().ToString("N");
        var guests = $"role:guests:{suffix}";
        var person = $"person:padded:{suffix}";
        var resource = $"resource:padded:{suffix}";
        await Roles.Define(guests, "Guests", [P]);
        await Roles.Add(guests, person);
        await Roles.Overwrite(resource, OverwriteSubjectKind.Role, guests, [], [P]);
        using var scope = _fixture.Services.CreateScope();
        var decision = await scope.ServiceProvider.GetRequiredService<AccessExplainer>().Explain(person, resource, " global:post ");
        decision.Allowed.Should().BeFalse();
        decision.ContributingFacts.Should().ContainSingle().Which.Should().Match<AccessFact>(f =>
            f.Kind == AccessFactKind.Deny && f.Value == P && f.Subject == guests);
    }

    [Fact]
    public async Task Why_compares_an_overwrite_resource_id_exactly_as_resolution_does()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var person = $"person:why:{suffix}";
        var member = $"role:why:{suffix}";
        await new Identity { Id = person, DisplayName = "Why" }.Save();
        await Roles.Define(member, "Member", [View]);
        await Roles.Add(member, person);
        await Roles.Overwrite($"Board:1:{suffix}", OverwriteSubjectKind.Person, person, [], [View]);

        using var scope = _fixture.Services.CreateScope();
        var explainer = scope.ServiceProvider.GetRequiredService<AccessExplainer>();
        (await explainer.WhyAsync(person, $"board:1:{suffix}")).Should().NotContain(f => f.Source == nameof(PermissionOverwrite));
        (await explainer.Explain(person, $"board:1:{suffix}", View)).Allowed.Should().BeTrue();
        (await explainer.WhyAsync(person, $"Board:1:{suffix}")).Should().Contain(f =>
            f.Source == nameof(PermissionOverwrite) && f.Kind == AccessFactKind.Deny && f.Value == View);
        (await explainer.WhyAsync(person, $"  Board:1:{suffix} ")).Should().Contain(f =>
            f.Source == nameof(PermissionOverwrite) && f.Kind == AccessFactKind.Deny && f.Value == View,
            "a padded resource id is trimmed at entry, as Explain trims it");
        (await explainer.Explain(person, $"  Board:1:{suffix} ", View)).Allowed.Should().BeFalse();
        (await explainer.Explain(person, $"Board:1:{suffix}", View)).Allowed.Should().BeFalse();
    }

    [Fact]
    public async Task Why_names_the_overwrites_on_the_resource_asked_about_and_skips_an_expired_one()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var person = $"person:contributor:{suffix}";
        var member = $"role:contributor:{suffix}";
        await new Identity { Id = person, DisplayName = "Contributor" }.Save();
        await Roles.Define(member, "Member", [View]);
        await Roles.Add(member, person);
        await Roles.Overwrite($"resource:a:{suffix}", OverwriteSubjectKind.Role, member, [P], [View]);
        await Roles.Overwrite($"resource:b:{suffix}", OverwriteSubjectKind.Person, person, ["global:pin"], []);
        await Roles.Define($"role:not-held:{suffix}", "Not held", []);
        await Roles.Overwrite($"resource:c:{suffix}", OverwriteSubjectKind.Role, $"role:not-held:{suffix}", [P], []);
        await Roles.Overwrite($"resource:d:{suffix}", OverwriteSubjectKind.Person, person, ["global:short_lived"], [], DateTimeOffset.UtcNow.AddMilliseconds(300));
        await Task.Delay(400, TestContext.Current.CancellationToken);

        using var scope = _fixture.Services.CreateScope();
        var explainer = scope.ServiceProvider.GetRequiredService<AccessExplainer>();
        OverwriteFacts(await explainer.WhyAsync(person, $"resource:a:{suffix}")).Should().BeEquivalentTo(new[]
        {
            new { Kind = AccessFactKind.Permission, Value = P, Resource = $"resource:a:{suffix}", Subject = member },
            new { Kind = AccessFactKind.Deny, Value = View, Resource = $"resource:a:{suffix}", Subject = member },
        });
        OverwriteFacts(await explainer.WhyAsync(person, $"resource:b:{suffix}")).Should().BeEquivalentTo(new[]
        {
            new { Kind = AccessFactKind.Permission, Value = "global:pin", Resource = $"resource:b:{suffix}", Subject = person },
        });
        OverwriteFacts(await explainer.WhyAsync(person, $"resource:c:{suffix}")).Should().BeEmpty("the person does not hold that role");
        OverwriteFacts(await explainer.WhyAsync(person, $"resource:d:{suffix}")).Should().BeEmpty("the overwrite expired");
        OverwriteFacts(await explainer.WhyAsync(person, $"resource:a:{suffix}")).Should().OnlyContain(f => f.RowType == nameof(PermissionOverwrite));

        var access = await scope.ServiceProvider.GetRequiredService<EffectiveAccessResolver>().ResolveAsync(person);
        access.Facts.Should().NotContain(f => f.Source == nameof(PermissionOverwrite),
            "an overwrite holds on one resource, so it is asked of that resource, never listed for every resource");
        access.Capabilities.Should().Contain(View).And.NotContain("global:pin");

        static IEnumerable<AccessFact> OverwriteFacts(IReadOnlyList<AccessFact> facts) => facts.Where(f => f.Source == nameof(PermissionOverwrite));
    }

    /// <summary>
    /// Reading only the rows a bag can meet gives the answer every overwrite on the resource gives: the same
    /// permissions and the same deciding fact, for a person in several roles, a person in none, and a visitor.
    /// </summary>
    [Fact]
    public async Task OverwritesFor_resolves_as_every_overwrite_on_the_resource_does()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var resource = $"resource:equivalent:{suffix}";
        string Role(string name) => $"role:{name}:{suffix}";
        string Person(string name) => $"person:{name}:{suffix}";
        foreach (var name in new[] { "a", "b", "c" }) await Roles.Define(Role(name), name, [View]);
        await Roles.Add(Role("a"), Person("one"));
        await Roles.Add(Role("a"), Person("two"));
        await Roles.Add(Role("b"), Person("two"));
        await Roles.Overwrite(resource, OverwriteSubjectKind.Role, RoleTokens.Everyone, [View], [P]);
        await Roles.Overwrite(resource, OverwriteSubjectKind.Role, Role("a"), [P], []);
        await Roles.Overwrite(resource, OverwriteSubjectKind.Role, Role("b"), [], [View, "global:pin"]);
        await Roles.Overwrite(resource, OverwriteSubjectKind.Role, Role("c"), ["global:pin"], [P]);
        await Roles.Overwrite(resource, OverwriteSubjectKind.Person, Person("one"), [], [P]);
        await Roles.Overwrite(resource, OverwriteSubjectKind.Person, Person("three"), ["global:pin"], []);
        await Roles.Overwrite(resource, OverwriteSubjectKind.Person, Person("elsewhere"), [], [View]);

        var every = new List<PermissionOverwrite>();
        for (string? after = null; ;)
        {
            var page = await Roles.Overwrites(resource, after, take: 2);
            every.AddRange(page.Items);
            if ((after = page.Next) is null) break;
        }

        every.Should().HaveCount(7);
        var now = DateTimeOffset.UtcNow;
        foreach (var bag in new[] { await Roles.Bag(Person("one"), true), await Roles.Bag(Person("two"), true), await Roles.Bag(Person("three"), true), await Roles.Bag(null, false) })
        {
            var full = bag.On(resource, every, now);
            var lean = bag.On(resource, await Roles.OverwritesFor(bag, resource), now);
            lean.Permissions.Should().Equal(full.Permissions, bag.PersonId ?? "visitor");
            foreach (var permission in new[] { P, View, "global:pin", "global:unknown" })
                lean.Explain(permission).Should().BeEquivalentTo(full.Explain(permission), $"{bag.PersonId ?? "visitor"} {permission}");
        }
    }

    /// <summary>The administration listings read a page at a time: a resource's overwrites, roles first by key and then
    /// people by id, and a subject's across resources in resource order.</summary>
    [Fact]
    public async Task A_resources_and_a_subjects_overwrites_page_with_a_cursor()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var resource = $"resource:listed:{suffix}";
        var roleKeys = new[] { $"role:x:{suffix}", $"role:y:{suffix}" };
        foreach (var key in roleKeys) await Roles.Define(key, key, []);
        var people = Enumerable.Range(0, 5).Select(i => $"person:listed:{suffix}:{i}").ToArray();
        await Roles.Overwrite(resource, OverwriteSubjectKind.Role, RoleTokens.Everyone, [View], []);
        foreach (var key in roleKeys.Reverse()) await Roles.Overwrite(resource, OverwriteSubjectKind.Role, key, [P], []);
        foreach (var person in people.Reverse()) await Roles.Overwrite(resource, OverwriteSubjectKind.Person, person, [], [P]);

        var listed = new List<string>();
        for (string? after = null; ;)
        {
            var page = await Roles.Overwrites(resource, after, take: 3);
            listed.AddRange(page.Items.Select(row => $"{row.SubjectKind}/{row.SubjectId}"));
            if ((after = page.Next) is null) break;
        }

        string[] roleSubjects = [RoleTokens.Everyone, .. roleKeys];
        listed.Should().Equal([.. roleSubjects.Order(StringComparer.Ordinal).Select(key => $"Role/{key}"), .. people.Select(person => $"Person/{person}")],
            "roles first by key, then people by id, each once");

        var subject = $"person:many-resources:{suffix}";
        var places = Enumerable.Range(0, 7).Select(i => $"resource:place:{suffix}:{i}").ToArray();
        foreach (var place in places.Reverse()) await Roles.Overwrite(place, OverwriteSubjectKind.Person, subject, [View], []);
        var pages = new List<IReadOnlyList<string>>();
        for (string? after = null; ;)
        {
            var page = await Roles.OverwritesOf(OverwriteSubjectKind.Person, subject, after, take: 3);
            pages.Add(page.Items.Select(row => row.ResourceId).ToArray());
            if ((after = page.Next) is null) break;
        }

        pages.SelectMany(page => page).Should().Equal(places);
        pages.Select(page => page.Count).Should().Equal(3, 3, 1);

        var unknownCursor = () => Roles.Overwrites(resource, "Group:anything");
        await unknownCursor.Should().ThrowAsync<ArgumentException>();
        var tooMany = () => Roles.OverwritesOf(OverwriteSubjectKind.Person, subject, take: 101);
        await tooMany.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Malformed_overwrites_are_refused_before_any_write()
    {
        var resource = $"resource:malformed:{Guid.NewGuid():N}";
        Func<Task> administrator = () => Roles.Overwrite(resource, OverwriteSubjectKind.Role, RoleTokens.Everyone, [RoleTokens.Administrator], []);
        Func<Task> notPermission = () => Roles.Overwrite(resource, OverwriteSubjectKind.Role, RoleTokens.Everyone, ["post"], []);
        Func<Task> empty = () => Roles.Overwrite(resource, OverwriteSubjectKind.Role, RoleTokens.Everyone, [], []);
        Func<Task> past = () => Roles.Overwrite(resource, OverwriteSubjectKind.Role, RoleTokens.Everyone, [P], [], DateTimeOffset.UtcNow.AddMinutes(-1));
        Func<Task> unknownKind = () => Roles.Overwrite(resource, (OverwriteSubjectKind)7, "x", [P], []);
        Func<Task> unknownRole = () => Roles.Overwrite(resource, OverwriteSubjectKind.Role, $"role:missing:{Guid.NewGuid():N}", [P], []);

        (await administrator.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("bypasses overwrites");
        (await notPermission.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("is not a permission");
        await empty.Should().ThrowAsync<ArgumentException>();
        await past.Should().ThrowAsync<ArgumentException>();
        await unknownKind.Should().ThrowAsync<ArgumentException>();
        await unknownRole.Should().ThrowAsync<KeyNotFoundException>();
        (await Roles.Overwrites(resource)).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task An_overwrite_change_invalidates_what_it_affects_and_leaves_bags_valid()
    {
        var cache = _fixture.Services.GetRequiredService<RoleBagCache>();
        var ct = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var person = $"person:cache:{suffix}";
        var resource = $"resource:cache:{suffix}";
        var member = $"role:cache:{suffix}";
        await Roles.Define(member, "Member", [P]);
        await Roles.Add(member, person);
        await Roles.Overwrite(resource, OverwriteSubjectKind.Role, RoleTokens.Everyone, [], [P]);

        var compiled = await cache.RoleOverwrites(resource, ct);
        (await cache.RoleOverwrites(resource, ct)).Should().BeSameAs(compiled, "a warm read is cache-only");
        var bag = await Roles.Bag(person, true);
        bag.On(resource, await Roles.OverwritesFor(bag, resource), DateTimeOffset.UtcNow).Holds(P).Should().BeFalse();

        await Roles.Overwrite(resource, OverwriteSubjectKind.Person, person, [P], []);

        (await cache.RoleOverwrites(resource, ct)).Should().BeSameAs(compiled, "a person's own overwrite leaves the resource's role overwrites compiled");
        (await Roles.Bag(person, true)).Should().BeSameAs(bag, "a bag holds no overwrite, so an overwrite change leaves it valid");
        (await Roles.OverwritesFor(bag, resource)).Should().HaveCount(2);
        bag.On(resource, await Roles.OverwritesFor(bag, resource), DateTimeOffset.UtcNow).Holds(P).Should().BeTrue();

        await Roles.Overwrite(resource, OverwriteSubjectKind.Role, member, ["global:pin"], []);
        (await cache.RoleOverwrites(resource, ct)).Should().NotBeSameAs(compiled, "a role overwrite change compiles the resource's role overwrites again")
            .And.HaveCount(2);

        await Roles.RemoveOverwrite(resource, OverwriteSubjectKind.Person, person);
        bag.On(resource, await Roles.OverwritesFor(bag, resource), DateTimeOffset.UtcNow).Holds(P).Should().BeFalse("a removal reaches the next read");

        var warm = await cache.RoleOverwrites(resource, ct);
        cache.InvalidateOverwrites(resource);
        (await cache.RoleOverwrites(resource, ct)).Should().NotBeSameAs(warm, "a host propagating a change elsewhere invalidates the resource");
    }

    [Fact]
    public async Task Overwrite_events_veto_and_fire_once_and_the_guard_refuses_generic_writes()
    {
        var resource = $"resource:events:{Guid.NewGuid():N}";
        var changed = 0; var removed = 0;
        Koan.Data.Core.Model.Entity.Role.Reset()
            .OverwriteChanging(context => ValueTask.FromResult(context.Current.Deny.Contains("global:vetoed")
                ? RoleChangeDecision.Veto("overwrite.vetoed", "Vetoed by the application.")
                : RoleChangeDecision.Continue()))
            .OverwriteChanged(context => { changed++; context.Phase.Should().Be(RoleChangePhase.After); return ValueTask.CompletedTask; })
            .OverwriteRemoved(_ => { removed++; return ValueTask.CompletedTask; });
        try
        {
            var vetoed = () => Roles.Overwrite(resource, OverwriteSubjectKind.Role, RoleTokens.Everyone, [], ["global:vetoed"]);
            await vetoed.Should().ThrowAsync<UnauthorizedAccessException>();
            (await Roles.Overwrites(resource)).Items.Should().BeEmpty();

            await Roles.Overwrite(resource, OverwriteSubjectKind.Role, RoleTokens.Everyone, [], [P]);
            await Roles.Overwrite(resource, OverwriteSubjectKind.Role, RoleTokens.Everyone, [], [P]);
            (await Roles.RemoveOverwrite(resource, OverwriteSubjectKind.Role, RoleTokens.Everyone)).Should().BeTrue();
            (await Roles.RemoveOverwrite(resource, OverwriteSubjectKind.Role, RoleTokens.Everyone)).Should().BeFalse();
            changed.Should().Be(1); removed.Should().Be(1);
        }
        finally { Koan.Data.Core.Model.Entity.Role.Reset(); }

        var generic = () => Row(OverwriteSubjectKind.Role, RoleTokens.Everyone, Deny()).Save();
        (await generic.Should().ThrowAsync<EntityLifecycleCancelledException>()).Which.ReasonCode.Should().Be("role.mutation.guard");
    }

    [Fact]
    public async Task A_lifecycle_handler_that_changes_an_overwrite_is_refused_instead_of_waiting_on_the_lock()
    {
        var resource = $"resource:recursive:{Guid.NewGuid():N}";
        Koan.Data.Core.Model.Entity.Role.Reset().OverwriteChanging(async context =>
        {
            await Roles.Overwrite(resource, OverwriteSubjectKind.Person, "person:nested", [P], []);
            return RoleChangeDecision.Continue();
        });
        try
        {
            var change = () => Roles.Overwrite(resource, OverwriteSubjectKind.Role, RoleTokens.Everyone, [], [P])
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await change.Should().ThrowAsync<InvalidOperationException>().WithMessage("Recursive role mutation*");
        }
        finally { Koan.Data.Core.Model.Entity.Role.Reset(); }
        (await Roles.Overwrites(resource)).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Deleting_a_role_removes_its_overwrites_and_the_implicit_roles_keep_theirs()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var resource = $"resource:delete:{suffix}";
        var doomed = $"role:doomed:{suffix}";
        await Roles.Define(doomed, "Doomed", [P]);
        await Roles.Overwrite(resource, OverwriteSubjectKind.Role, doomed, [], [P]);
        await Roles.Overwrite(resource, OverwriteSubjectKind.Role, RoleTokens.Everyone, [View], []);

        (await Roles.Delete(doomed)).Should().BeTrue();
        (await Roles.Overwrites(resource)).Items.Should().ContainSingle().Which.SubjectId.Should().Be(RoleTokens.Everyone);
        await Roles.Define(doomed, "Doomed again", [P]);
        (await Roles.Overwrites(resource)).Items.Should().ContainSingle("a role defined again under the key starts with no overwrite");
    }

    private static RoleBag Bag(string[] permissions) => new(Person, true, [HeldRole], permissions);

    private static PermissionOverwrite Row(OverwriteSubjectKind kind, string subject, (string[] Allow, string[] Deny) grant,
        DateTimeOffset? expiresAt = null) => new()
    {
        Id = PermissionOverwrite.KeyFor(Resource, kind, subject),
        ResourceId = Resource,
        SubjectKind = kind,
        SubjectId = subject,
        Allow = [.. grant.Allow],
        Deny = [.. grant.Deny],
        ExpiresAt = expiresAt,
    };

    private static (string[] Allow, string[] Deny) Allow(string permission = P) => ([permission], []);
    private static (string[] Allow, string[] Deny) Deny(string permission = P) => ([], [permission]);

    private sealed record Case(string[] Base, (string[] Allow, string[] Deny)? Everyone, (string[] Allow, string[] Deny)? HeldRole,
        (string[] Allow, string[] Deny)? Member, bool Holds, string? DecidingSubject);
}
