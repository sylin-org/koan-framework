using System.Net;
using AwesomeAssertions;
using Koan.Web.Auth.Atproto.Tests;
using Xunit;

namespace Koan.Identity.Logins.Tests;

/// <summary>
/// SEC-0040 through the atproto connector: its browser callback binds a login to the signed-in person, and the
/// service-auth bridge lands a proved DID on the person its login is bound to, or makes one.
/// </summary>
public sealed class AtprotoLoginSpec
{
    private static readonly string One = AtprotoHost.Did('a');
    private static readonly string Two = AtprotoHost.Did('b');
    private static readonly string Three = AtprotoHost.Did('c');

    [Fact]
    public async Task A_new_atproto_login_binds_to_the_signed_in_person_whose_session_carries_its_did_and_handle()
    {
        await using var host = await AtprotoHost.Start(One, Two);
        using var browser = host.Browser();
        var person = await host.SignIn(browser, One);
        Guid.Parse(person.Id!).Version.Should().Be(7);
        person.Did.Should().Be(One);

        var bound = await host.Bind(browser, Two);

        bound.Id.Should().Be(person.Id, "the session continues as the person");
        (bound.Did, bound.Handle, bound.ProviderSubject).Should().Be((Two, AtprotoHost.HandleOf(Two), $"atproto:{Two}"),
            "an application sees which account signed in");
        using (var later = host.Browser()) (await host.SignIn(later, Two)).Id.Should().Be(person.Id);
        using var earlier = host.Browser();
        (await host.SignIn(earlier, One)).Id.Should().NotBe(person.Id, "the replaced DID arrives as a new person");
    }

    [Fact]
    public async Task A_page_of_another_site_cannot_bind_its_account_to_a_signed_in_person()
    {
        // The attack: a page elsewhere sends a signed-in person to bind an account its author holds at a provider that
        // approves anything, and would then sign in as them.
        await using var host = await AtprotoHost.Start(One, Two);
        using var browser = host.Browser();
        var person = await host.SignIn(browser, One);

        var posted = await browser.Submit(AtprotoHost.BindPath, AtprotoHost.BindFields(Two), site: "cross-site");
        posted.Status.Should().Be(HttpStatusCode.Forbidden, posted.Trace);
        var navigated = await browser.Go($"{AtprotoHost.BindPath}?identifier={AtprotoHost.HandleOf(Two)}");
        navigated.Status.Should().Be(HttpStatusCode.MethodNotAllowed, "a navigation cannot start a binding");

        var now = await browser.WhoAmI();
        (now.Id, now.Did).Should().Be((person.Id, One), "nothing was bound and the session is unchanged");
        using (var later = host.Browser()) (await host.SignIn(later, One)).Id.Should().Be(person.Id, "the person keeps their login");
        (await host.Bind(browser, Two)).Id.Should().Be(person.Id, "the application's own form still binds");
    }

    [Fact]
    public async Task With_no_failure_landing_another_persons_did_is_refused_with_a_problem_saying_why()
    {
        await using var host = await AtprotoHost.Start(One, Two);
        using (var owner = host.Browser()) await host.SignIn(owner, One);
        using var browser = host.Browser();
        var person = await host.SignIn(browser, Two);

        var refused = await host.Approved(browser, await browser.Submit(AtprotoHost.BindPath, AtprotoHost.BindFields(One)), One);

        refused.Status.Should().Be(HttpStatusCode.Conflict, refused.Trace);
        refused.Body.Should().Contain("login_taken");
        (await browser.WhoAmI()).Id.Should().Be(person.Id, "a refused binding signs nothing in");
    }

    [Fact]
    public async Task A_proof_of_a_bound_did_lands_on_its_person_and_a_proof_of_an_unbound_one_makes_a_person()
    {
        await using var host = await AtprotoHost.Start(One, Three);
        using var browser = host.Browser();
        var person = await host.SignIn(browser, One);
        var signer = new ServiceAuthSigner("secp256r1");
        host.Pds.SigningKey(One, signer.Method(One));
        host.Pds.SigningKey(Three, signer.Method(Three));

        using var agent = host.Browser();
        (await agent.Post(LoginProbeController.Prove, bearer: signer.Token(One, LoginProbeController.Audience, LoginProbeController.Method)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var proved = await agent.WhoAmI();
        (proved.Id, proved.Did, proved.ProviderSubject).Should().Be((person.Id, One, $"atproto:{One}"),
            "a proof signs in as the browser's callback does, as the person the DID's login is bound to");

        using var stranger = host.Browser();
        (await stranger.Post(LoginProbeController.Prove, bearer: signer.Token(Three, LoginProbeController.Audience, LoginProbeController.Method)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var made = await stranger.WhoAmI();
        made.Id.Should().NotBe(person.Id);
        Guid.Parse(made.Id!).Version.Should().Be(7, "an unbound DID's proof makes a person of Koan's own");
        made.Did.Should().Be(Three);
    }
}
