using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Web.Auth.Server.Protocol;
using Xunit;
using static Koan.Web.Auth.Server.IntegrationTests.OAuthTestFlows;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// SEC-0028 — a server-rendered consent page reads Koan's answer: <see cref="ConsentPage.Find"/> finds a code-flow
/// request by its <c>rid</c> and a Device-flow request by a user code typed in lower case without its hyphen, with the
/// client, scopes and resource, and with the approve and deny paths under the base path, which then work. The done page
/// reads the outcome of an approval and of a denial from its own address; a denial needs a signed-in person, so every
/// outcome names one; a plain, altered or other person's outcome reads as none.
/// </summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class ConsentPageSpec : IClassFixture<ConsentPageFixture>
{
    private const string Base = ConsentPageFixture.Base;
    private const string Redirect = "http://127.0.0.1:7851/cb";
    private readonly ConsentPageFixture _fx;

    public ConsentPageSpec(ConsentPageFixture fx) => _fx = fx;

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return doc.RootElement.Clone();
    }

    private async Task<HttpClient> SignedIn(string subject = "alice")
    {
        var browser = _fx.NewClient();
        (await browser.GetAsync($"{Base}/test/signin?sub={subject}", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        return browser;
    }

    /// <summary>Starts a device request and answers its user code as the device shows it.</summary>
    private async Task<string> StartDeviceRequest(string clientId)
    {
        await RegisterDeviceClient(_fx.Services, clientId, name: "Notes Agent");
        using var device = _fx.NewClient();
        var (_, userCode) = await StartDevice(device, Base, clientId, ConsentPageFixture.Url);
        return userCode;
    }

    /// <summary>The done page's path and query, from the absolute done URL a decision redirected to.</summary>
    private static string DonePage(HttpResponseMessage decided)
    {
        decided.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = decided.Headers.Location!;
        location.ToString().Should().StartWith($"{ConsentPageFixture.Url}/consent/done?{ConsentPage.OutcomeParameter}=");
        return location.PathAndQuery;
    }

    /// <summary>A string member, or null when the page's JSON omits it or writes null.</summary>
    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;

    private static async Task<string?> OutcomeOn(HttpClient browser, string donePage)
        => Text(await Json(await browser.GetAsync(donePage, Ct)), "outcome");

    [Fact]
    public async Task The_consent_page_finds_a_code_flow_request_by_rid_and_its_approve_path_works()
    {
        await RegisterCodeClient(_fx.Services, "notes-code", Redirect, name: "Notes Client");
        var (_, challenge) = Pkce();
        using var browser = await SignedIn();
        using var authorize = await browser.GetAsync(Authorize(Base, "notes-code", Redirect, challenge, ConsentPageFixture.Url), Ct);
        var consentPage = authorize.Headers.Location!.ToString();
        consentPage.Should().StartWith($"{Base}/consent?rid=");
        var rid = Query(consentPage, "rid");

        var pending = await Json(await browser.GetAsync(consentPage, Ct));

        pending.GetProperty("id").GetString().Should().Be(rid);
        pending.GetProperty("clientId").GetString().Should().Be("notes-code");
        pending.GetProperty("clientName").GetString().Should().Be("Notes Client");
        pending.GetProperty("scopes").EnumerateArray().Should().BeEmpty("signed in, the page shows what the person holds of what was requested");
        pending.GetProperty("resource").GetString().Should().Be(ConsentPageFixture.Url);
        pending.GetProperty("redirectUri").GetString().Should().Be(Redirect);
        Text(pending, "userCode").Should().BeNull();
        pending.GetProperty("approve").GetString().Should().Be($"{Base}/oauth/request/{rid}/approve");
        pending.GetProperty("deny").GetString().Should().Be($"{Base}/oauth/request/{rid}/deny");

        using var approved = await browser.PostAsync(pending.GetProperty("approve").GetString(), new FormUrlEncodedContent([]), Ct);
        approved.StatusCode.Should().Be(HttpStatusCode.Redirect);
        Query(approved.Headers.Location!.ToString(), "code").Should().NotBeEmpty();
    }

    [Fact]
    public async Task The_consent_page_finds_a_device_request_by_a_user_code_typed_in_lower_case_without_its_hyphen()
    {
        var userCode = await StartDeviceRequest("notes-device-lookup");
        var typed = userCode.Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
        using var browser = await SignedIn();

        var pending = await Json(await browser.GetAsync($"{Base}/consent?user_code={typed}", Ct));

        var normalized = userCode.Replace("-", "", StringComparison.Ordinal);
        pending.GetProperty("id").GetString().Should().Be(normalized);
        pending.GetProperty("userCode").GetString().Should().Be(userCode);
        pending.GetProperty("clientName").GetString().Should().Be("Notes Agent");
        Text(pending, "redirectUri").Should().BeNull();
        pending.GetProperty("approve").GetString().Should().Be($"{Base}/oauth/request/{normalized}/approve");

        using var approved = await browser.PostAsync(pending.GetProperty("approve").GetString(), new FormUrlEncodedContent([]), Ct);
        (await OutcomeOn(browser, DonePage(approved))).Should().Be(nameof(ConsentOutcome.Approved));
    }

    [Fact]
    public async Task An_unknown_id_finds_nothing()
    {
        using var browser = await SignedIn();
        (await browser.GetAsync($"{Base}/consent?user_code=bcdfghjk", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await browser.GetAsync($"{Base}/consent?rid=" + new string('x', 200), Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_done_page_reads_a_denial()
    {
        var userCode = await StartDeviceRequest("notes-device-deny");
        using var browser = await SignedIn();

        using var denied = await Decide(browser, Base, userCode, "deny");

        (await OutcomeOn(browser, DonePage(denied))).Should().Be(nameof(ConsentOutcome.Denied));
    }

    [Fact]
    public async Task A_device_denial_requires_a_signed_in_person()
    {
        var userCode = await StartDeviceRequest("notes-device-anonymous-deny");
        using var visitor = _fx.NewClient();

        using var denied = await Decide(visitor, Base, userCode, "deny");

        denied.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await denied.Content.ReadAsStringAsync(Ct)).Should().Contain("login_required");
        using var browser = await SignedIn();
        (await browser.GetAsync($"{Base}/consent?user_code={userCode}", Ct)).StatusCode.Should().Be(HttpStatusCode.OK,
            "the request still waits for a person's decision");
    }

    [Fact]
    public async Task A_forged_altered_or_other_persons_outcome_reads_as_none()
    {
        var userCode = await StartDeviceRequest("notes-device-forged");
        using var alice = await SignedIn("alice");
        using var approved = await Decide(alice, Base, userCode, "approve");
        var genuine = DonePage(approved);
        var value = Query(genuine, ConsentPage.OutcomeParameter);
        var altered = value[..^2] + (value.EndsWith("AA", StringComparison.Ordinal) ? "BB" : "AA");

        (await OutcomeOn(alice, genuine)).Should().Be(nameof(ConsentOutcome.Approved), "the person who decided reads it");
        (await OutcomeOn(alice, $"{Base}/consent/done?{ConsentPage.OutcomeParameter}=Approved")).Should().BeNull("a plain value is not Koan's");
        (await OutcomeOn(alice, $"{Base}/consent/done?{ConsentPage.OutcomeParameter}={Uri.EscapeDataString(altered)}")).Should().BeNull();
        (await OutcomeOn(alice, $"{Base}/consent/done")).Should().BeNull();

        using var bob = await SignedIn("bob");
        (await OutcomeOn(bob, genuine)).Should().BeNull("an approval link sent to another person claims nothing for them");
        using var visitor = _fx.NewClient();
        (await OutcomeOn(visitor, genuine)).Should().BeNull();
    }
}
