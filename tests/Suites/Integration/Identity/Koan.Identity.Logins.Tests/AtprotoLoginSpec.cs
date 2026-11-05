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
