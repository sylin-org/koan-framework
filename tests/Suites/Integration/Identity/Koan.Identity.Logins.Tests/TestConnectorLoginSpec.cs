using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Xunit;

namespace Koan.Identity.Logins.Tests;

/// <summary>
/// SEC-0040 through real sign-ins of the Test connector: a person is Koan's own, binds a login of another provider or a
/// new one of the same provider, is refused another person's login, needs a recent sign-in to bind, and never unbinds
/// their last login.
/// </summary>
public sealed class TestConnectorLoginSpec(TestConnectorHost host) : IClassFixture<TestConnectorHost>
{
    private static int _people;

    [Fact]
    public async Task A_first_sign_in_makes_a_person_whose_id_is_koans_own_and_the_next_arrives_as_them()
    {
        var email = Unique("ann");
        using var browser = host.Browser(email);

        var ann = await SignIn(browser, "test");

        Guid.Parse(ann.Id!).Version.Should().Be(7, "the person's id is Koan's own, a GUID v7, never the provider's subject");
        ann.ProviderSubject.Should().Be($"test:{email}", "the session still says which login signed in");
        using (AppHost.PushScope(host.Services))
        {
            (await Identity.Get(ann.Id!)).Should().NotBeNull();
            (await IdentityEmail.Query(e => e.IdentityId == ann.Id)).Should().ContainSingle(e => e.Address == email && e.Verified);
        }
        using var later = host.Browser(email);
        (await SignIn(later, "test")).Id.Should().Be(ann.Id, "a returning login arrives as its person");
    }

    [Fact]
    public async Task Concurrent_first_sign_ins_of_one_login_end_on_one_person()
    {
        var email = Unique("ben");
        var browsers = Enumerable.Range(0, 4).Select(_ => host.Browser(email)).ToArray();
        try
        {
            var people = await Task.WhenAll(browsers.Select(browser => SignIn(browser, "test")));

            people.Select(person => person.Id).Distinct().Should().ContainSingle("the login is written only if absent, and every loser reads the winner");
            using (AppHost.PushScope(host.Services))
                (await Identity.Query(person => person.Id == people[0].Id)).Should().ContainSingle();
        }
        finally { foreach (var browser in browsers) browser.Dispose(); }
    }

    [Fact]
    public async Task A_signed_in_person_binds_a_login_of_a_second_provider_and_signs_in_with_either()
    {
        var first = Unique("cal");
        var second = Unique("cal-oidc");
        using var browser = host.Browser(first);
        var cal = await SignIn(browser, "test");

        var bound = await Bind(browser, "test-oidc", second);

        bound.At.AbsolutePath.Should().Be(LoginProbeController.WhoAmI, bound.Trace);
        var now = await browser.WhoAmI();
        now.Id.Should().Be(cal.Id, "the session continues as the person");
        now.ProviderSubject.Should().Be($"test-oidc:{second}", "carrying the new login's claims");
        using (var other = host.Browser(second)) (await SignIn(other, "test-oidc")).Id.Should().Be(cal.Id);
        using (var again = host.Browser(first)) (await SignIn(again, "test")).Id.Should().Be(cal.Id);
        (await Logins(cal.Id!)).Should().BeEquivalentTo(["test", "test-oidc"]);
    }

    [Fact]
    public async Task Binding_a_new_login_of_a_provider_replaces_the_earlier_one_whose_subject_then_arrives_as_a_new_person()
    {
        var earlier = Unique("dot");
        var newer = Unique("dot-new");
        using var browser = host.Browser(earlier);
        var dot = await SignIn(browser, "test");

        (await Bind(browser, "test", newer)).At.AbsolutePath.Should().Be(LoginProbeController.WhoAmI);

        (await browser.WhoAmI()).Should().Be(dot with { ProviderSubject = $"test:{newer}" });
        (await Logins(dot.Id!)).Should().Equal("test");
        using (var stranger = host.Browser(earlier))
            (await SignIn(stranger, "test")).Id.Should().NotBe(dot.Id, "the replaced login is no longer the person's");
        using (var same = host.Browser(newer)) (await SignIn(same, "test")).Id.Should().Be(dot.Id);
    }

    [Fact]
    public async Task A_login_that_is_another_persons_is_refused_saying_why_and_stays_theirs()
    {
        var theirs = Unique("eve");
        using var owner = host.Browser(theirs);
        var eve = await SignIn(owner, "test");
        using var browser = host.Browser(Unique("fox"));
        var fox = await SignIn(browser, "test");

        var refused = await Bind(browser, "test", theirs);

        refused.At.AbsolutePath.Should().Be(LoginProbeController.LandingPath, refused.Trace);
        refused.At.Query.Should().Contain("error=login_taken");
        (await browser.WhoAmI()).Should().Be(fox, "a refused binding signs nothing in");
        (await Logins(fox.Id!)).Should().Equal("test");
        using var again = host.Browser(theirs);
        (await SignIn(again, "test")).Id.Should().Be(eve.Id, "nothing merges");
    }

    [Fact]
    public async Task Binding_needs_a_recent_sign_in_so_a_cookie_alone_binds_nothing()
    {
        using var nobody = host.Browser(Unique("gil"));
        (await nobody.Go("/me/connections/test/link")).Status.Should().Be(HttpStatusCode.Unauthorized, "binding needs someone signed in");

        var first = Unique("hal");
        using var browser = host.Browser(first);
        var hal = await SignIn(browser, "test");
        host.Clock.Offset += TimeSpan.FromMinutes(11);
        try
        {
            var stale = await Bind(browser, "test", Unique("hal-new"));
            stale.At.AbsolutePath.Should().Be(LoginProbeController.LandingPath, stale.Trace);
            stale.At.Query.Should().Contain("error=sign_in_first");
            (await browser.Go("/me/connections/test/link")).Status.Should().Be(HttpStatusCode.Unauthorized);
            (await Logins(hal.Id!)).Should().Equal("test");

            browser.Cookie("_tp_user", TestConnectorHost.Persona(first));
            (await SignIn(browser, "test")).Id.Should().Be(hal.Id);
            var newer = Unique("hal-new");
            (await Bind(browser, "test", newer)).At.AbsolutePath.Should().Be(LoginProbeController.WhoAmI, "a fresh sign-in binds");
            (await browser.WhoAmI()).ProviderSubject.Should().Be($"test:{newer}");
        }
        finally { host.Clock.Offset = TimeSpan.Zero; }
    }

    [Fact]
    public async Task A_person_unbinds_a_login_but_never_their_last()
    {
        var first = Unique("ida");
        using var browser = host.Browser(first);
        var ida = await SignIn(browser, "test");
        var only = (await Connected(browser)).Should().ContainSingle().Subject;

        var last = await browser.Delete($"/api/identity/me/connected/{only}");
        last.StatusCode.Should().Be(HttpStatusCode.Conflict, "a person signs in with a login");
        (await last.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("bind another");

        await Bind(browser, "test-oidc", Unique("ida-oidc"));
        (await browser.Delete($"/api/identity/me/connected/{only}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Logins(ida.Id!)).Should().Equal("test-oidc");
        using var earlier = host.Browser(first);
        (await SignIn(earlier, "test")).Id.Should().NotBe(ida.Id, "the unbound subject's next sign-in arrives as a new person");
    }

    [Fact]
    public async Task An_ordinary_sign_in_while_signed_in_replaces_the_session_and_binds_nothing()
    {
        using var browser = host.Browser(Unique("jo"));
        var jo = await SignIn(browser, "test");
        var other = Unique("kit");

        browser.Cookie("_tp_user", TestConnectorHost.Persona(other));
        var kit = await SignIn(browser, "test");

        kit.Id.Should().NotBe(jo.Id, "only the explicit intent binds");
        kit.ProviderSubject.Should().Be($"test:{other}");
        (await Logins(jo.Id!)).Should().Equal("test");
    }

    private static string Unique(string name) => $"{name}{Interlocked.Increment(ref _people)}x{Guid.NewGuid().ToString("N")[..6]}@k52.test";

    private static async Task<Who> SignIn(Browser browser, string provider)
    {
        var landing = await browser.Go($"/auth/{provider}/challenge?return={LoginProbeController.WhoAmI}");
        landing.Status.Should().Be(HttpStatusCode.OK, landing.Trace);
        landing.At.AbsolutePath.Should().Be(LoginProbeController.WhoAmI, landing.Trace);
        return await browser.WhoAmI();
    }

    /// <summary>Binds the login of <paramref name="provider"/> the Test provider signs <paramref name="email"/> in as.</summary>
    private static Task<Landing> Bind(Browser browser, string provider, string email)
    {
        browser.Cookie("_tp_user", TestConnectorHost.Persona(email));
        return browser.Go($"/me/connections/{provider}/link?return={LoginProbeController.WhoAmI}&failure={LoginProbeController.LandingPath}");
    }

    /// <summary>The providers of the logins <paramref name="person"/> holds.</summary>
    private async Task<IReadOnlyList<string>> Logins(string person)
    {
        using var scope = AppHost.PushScope(host.Services);
        return (await ExternalIdentityLink.Query(link => link.IdentityId == person)).Select(link => link.Provider).Order().ToList();
    }

    /// <summary>The ids of the signed-in person's logins, as the self-service console lists them.</summary>
    private static async Task<IReadOnlyList<string>> Connected(Browser browser)
    {
        var landing = await browser.Go("/api/identity/me/connected");
        landing.Status.Should().Be(HttpStatusCode.OK, landing.Trace);
        using var json = JsonDocument.Parse(landing.Body);
        return json.RootElement.EnumerateArray()
            .Select(link => link.EnumerateObject().First(property => property.NameEquals("id") || property.NameEquals("Id")).Value.GetString()!)
            .ToList();
    }
}
