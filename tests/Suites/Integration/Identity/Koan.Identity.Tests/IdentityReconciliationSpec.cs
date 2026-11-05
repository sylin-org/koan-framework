using System.Security.Claims;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Identity.Infrastructure;
using Koan.Identity.Initialization;
using Koan.Identity.Management;
using Koan.Identity.Reconciliation;
using Koan.Tenancy;
using Koan.Testing.Integration;
using Koan.Web.Auth.Contributors;
using Koan.Web.Auth.Domain;
using Koan.Web.Auth.Flow;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Identity.Tests;

/// <summary>
/// SEC-0007 P0 / Layer-0 and SEC-0040 acceptance (ARCH-0079 — real <c>AddKoan()</c>, offline). Proves the durable person
/// core + the wired sign-in reconciliation seams (the auth flow handler and the external-identity store), a person of
/// Koan's own id and the logins bound to it, the <c>Membership.IdentityId</c> soft-FK resolution, the ambient-exempt
/// global plane, and a boot that refuses invalid configuration.
/// </summary>
[Collection("identity")]
public sealed class IdentityReconciliationSpec : IdentityHostScopedSpec
{
    private readonly IdentityHostFixture _fx;
    public IdentityReconciliationSpec(IdentityHostFixture fx) : base(fx) => _fx = fx;

    private IIdentityReconciler Reconciler => _fx.Services.GetRequiredService<IIdentityReconciler>();

    [Fact]
    public void Identity_entities_are_ambient_exempt_global_plane()
    {
        // The person spans tenants — identity entities are the global plane, never tenant-stamped/filtered.
        TenantScopeMetadata.IsHostScopedType(typeof(Identity)).Should().BeTrue();
        TenantScopeMetadata.IsHostScopedType(typeof(IdentityEmail)).Should().BeTrue();
        TenantScopeMetadata.IsHostScopedType(typeof(ExternalIdentityLink)).Should().BeTrue();
        TenantScopeMetadata.IsHostScopedType(typeof(AuditEvent)).Should().BeTrue();
    }

    [Theory]
    [InlineData(typeof(Session), nameof(Session.IdentityId))]
    [InlineData(typeof(IdentityEmail), nameof(IdentityEmail.IdentityId))]
    [InlineData(typeof(ExternalIdentityLink), nameof(ExternalIdentityLink.IdentityId))]
    [InlineData(typeof(Koan.Identity.Impersonation.ImpersonationGrant), nameof(Koan.Identity.Impersonation.ImpersonationGrant.Actor))]
    [InlineData(typeof(Koan.Identity.Impersonation.ImpersonationGrant), nameof(Koan.Identity.Impersonation.ImpersonationGrant.Target))]
    [InlineData(typeof(Membership), nameof(Membership.IdentityId))]
    [InlineData(typeof(Membership), nameof(Membership.TenantId))]
    public void A_lookup_by_person_or_tenant_leads_an_index(Type entity, string property)
        => IndexMetadata.GetIndexes(entity).Should().Contain(
            index => !index.IsPrimaryKey && index.Properties[0].Name == property,
            "every /me and every sign-in reads these rows by this property");

    [Fact]
    public async Task Reconcile_upserts_durable_person_with_verified_primary_email()
    {
        var person = await Reconciler.ReconcileAsync(new IdentityClaims(
            "carol@example.com", DisplayName: "Carol", Picture: "https://x/c.png",
            Email: "Carol@Example.com", EmailVerified: true, Provider: "google"));

        Guid.Parse(person.Id).Version.Should().Be(7, "a first sign-in makes a person whose id is Koan's own, never the provider's subject");
        (await Identity.Get("carol@example.com")).Should().BeNull();

        var fetched = await Identity.Get(person.Id);
        fetched.Should().NotBeNull();
        fetched!.DisplayName.Should().Be("Carol");
        fetched.Picture.Should().Be("https://x/c.png");
        fetched.Status.Should().Be(IdentityStatus.Active);
        fetched.CreatedAt.Should().NotBe(default);

        var emails = await IdentityEmail.Query(e => e.IdentityId == person.Id);
        emails.Should().ContainSingle();
        emails[0].Address.Should().Be("carol@example.com"); // normalized lower-case
        emails[0].Verified.Should().BeTrue();
        emails[0].Primary.Should().BeTrue();
    }

    [Fact]
    public async Task Reconcile_is_idempotent_and_never_clobbers_user_fields()
    {
        await Reconciler.ReconcileAsync(new IdentityClaims("dave@example.com", DisplayName: "Dave", Email: "dave@example.com", EmailVerified: true));
        var first = await Identity.Get("dave@example.com");
        var created = first!.CreatedAt;

        // A later sign-in carrying a different IdP display name must NOT overwrite the established value.
        await Reconciler.ReconcileAsync(new IdentityClaims("dave@example.com", DisplayName: "David The Second", Email: "dave@example.com", EmailVerified: true));
        var second = await Identity.Get("dave@example.com");

        second!.DisplayName.Should().Be("Dave");   // backfill-only: not clobbered
        second.CreatedAt.Should().Be(created);     // creation stamp stable across upserts
        (await IdentityEmail.Query(e => e.IdentityId == "dave@example.com")).Should().ContainSingle(); // no duplicate factor
    }

    [Fact]
    public async Task Membership_IdentityId_resolves_to_the_durable_person()
    {
        await Reconciler.ReconcileAsync(new IdentityClaims("erin@example.com", DisplayName: "Erin", Email: "erin@example.com", EmailVerified: true));

        var m = new Membership { TenantId = "studio-a", IdentityId = "erin@example.com", Roles = new() { "koan:owner" } };
        await m.Save();

        var person = await Identity.Get(m.IdentityId);
        person.Should().NotBeNull("Membership.IdentityId is a soft-FK that must resolve to the durable Identity");
        person!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task IUserStore_is_replaced_and_reports_real_existence()
    {
        var store = _fx.Services.GetRequiredService<IUserStore>();
        store.Should().BeOfType<IdentityUserStore>();

        await Reconciler.ReconcileAsync(new IdentityClaims("ivan@example.com", DisplayName: "Ivan", Email: "ivan@example.com", EmailVerified: true));
        (await store.Exists("ivan@example.com")).Should().BeTrue();   // a real person exists
        (await store.Exists("no-such-person")).Should().BeFalse();    // the stub always returned false
    }

    [Fact]
    public async Task Auth_flow_handler_is_discovered_and_reconciles_on_signin()
    {
        using var scope = _fx.Services.CreateScope();
        // [KoanDiscoverable] registration: the reconciler must be a member of the dispatched handler set.
        var handler = scope.ServiceProvider.GetServices<IKoanAuthFlowHandler>()
            .OfType<IdentityAuthFlowHandler>().SingleOrDefault();
        handler.Should().NotBeNull("[KoanDiscoverable] must auto-register the reconciliation handler");
        handler!.Priority.Should().Be(1000, "reconciliation must run after identity-mapping handlers (int.MinValue)");
        var provider = Guid.NewGuid().ToString("N");

        var ci = new ClaimsIdentity(authenticationType: "test");
        ci.AddClaim(new Claim(ClaimTypes.NameIdentifier, "frank@example.com"));
        ci.AddClaim(new Claim(ClaimTypes.Name, "Frank"));
        ci.AddClaim(new Claim(ClaimTypes.Email, "frank@example.com"));
        ci.AddClaim(new Claim("email_verified", "true"));
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var ctx = new AuthSignInContext { Provider = provider, Identity = ci, Services = scope.ServiceProvider, HttpContext = http };

        // Route through the REAL dispatcher (Priority-ordered, membership-checked) — the same call the cookie
        // OnSigningIn event makes — not a direct handler invocation that would bypass ordering + registration.
        await scope.ServiceProvider.GetRequiredService<AuthFlowDispatcher>().DispatchSignIn(ctx, default);

        var subject = ci.FindFirst(ClaimTypes.NameIdentifier)!.Value;
        subject.Should().NotBe("frank@example.com", "the cookie's subject becomes the person's own id");
        var person = await Identity.Get(subject);
        person.Should().NotBeNull("the wired cookie sign-in dispatch must materialize the durable person");
        person!.DisplayName.Should().Be("Frank");
    }

    [Fact]
    public async Task Relink_same_provider_identity_upserts_one_row()
    {
        var store = _fx.Services.GetRequiredService<IExternalIdentityStore>();
        await store.Link(Login("heidi@example.com", "google", "{\"name\":\"Heidi\"}"));
        await store.Link(Login("heidi@example.com", "google", "{\"name\":\"Heidi\",\"avatar\":\"https://x/h.png\"}"));

        var person = await PersonOf("heidi@example.com", "google");
        var links = await store.GetByUser(person);
        links.Should().ContainSingle("the deterministic KeyFor id must upsert the same login, not append a duplicate");
        links[0].ClaimsJson.Should().Contain("avatar", "the surviving row reflects the latest sign-in");
    }

    [Fact]
    public async Task Concurrent_first_sign_ins_of_one_login_end_on_one_person()
    {
        var subject = $"racer-{Guid.CreateVersion7():N}";
        var people = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            Reconciler.ReconcileAsync(new IdentityClaims(subject, DisplayName: "Racer", Provider: "google")))));

        people.Select(person => person.Id).Distinct().Should().ContainSingle("the login is written only if absent, and the loser reads the winner");
        (await ExternalIdentityLink.Query(l => l.IdentityId == people[0].Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_person_stored_with_their_first_subject_as_id_keeps_arriving_as_themselves()
    {
        // Every person stored before SEC-0040 has the subject of their first login as id, and that login binds to it.
        const string subject = "did:plc:legacyaaaaaaaaaaaaaaaaaaa";
        await new Identity { Id = subject, DisplayName = "Legacy" }.Save();
        var hash = IdentityReconciler.ProviderKeyHash(subject);
        await new ExternalIdentityLink { Id = ExternalIdentityLink.KeyFor(subject, "atproto", hash), IdentityId = subject, Provider = "atproto", ProviderKeyHash = hash }.Save();

        (await Reconciler.ReconcileAsync(new IdentityClaims(subject, Provider: "atproto"))).Id.Should().Be(subject);
    }

    [Fact]
    public async Task Email_verification_upgrades_on_a_later_signin()
    {
        await Reconciler.ReconcileAsync(new IdentityClaims("upg@example.com", Email: "upg@example.com", EmailVerified: false));
        var before = await IdentityEmail.Query(e => e.IdentityId == "upg@example.com");
        before.Should().ContainSingle();
        before[0].Verified.Should().BeFalse();

        await Reconciler.ReconcileAsync(new IdentityClaims("upg@example.com", Email: "upg@example.com", EmailVerified: true));
        var after = await IdentityEmail.Query(e => e.IdentityId == "upg@example.com");
        after.Should().ContainSingle("no duplicate factor is created on the verification upgrade");
        after[0].Verified.Should().BeTrue("an unverified factor upgrades when the IdP later asserts it verified");
    }

    [Fact]
    public async Task Second_email_factor_is_not_primary()
    {
        await Reconciler.ReconcileAsync(new IdentityClaims("multi@example.com", Email: "first@example.com", EmailVerified: true));
        await Reconciler.ReconcileAsync(new IdentityClaims("multi@example.com", Email: "second@example.com", EmailVerified: true));

        var emails = await IdentityEmail.Query(e => e.IdentityId == "multi@example.com");
        emails.Should().HaveCount(2);
        emails.Single(e => e.Address == "first@example.com").Primary.Should().BeTrue("the first verified factor is primary");
        emails.Single(e => e.Address == "second@example.com").Primary.Should().BeFalse("a later factor is not auto-primary");
    }

    [Fact]
    public async Task Person_without_an_email_claim_is_still_materialized()
    {
        await Reconciler.ReconcileAsync(new IdentityClaims("noemail@example.com", DisplayName: "No Email"));

        (await Identity.Get("noemail@example.com")).Should().NotBeNull("an IdP that omits email must still yield a durable person");
        (await IdentityEmail.Query(e => e.IdentityId == "noemail@example.com")).Should().BeEmpty();
    }

    [Fact]
    public async Task External_identity_link_is_durable_relates_and_materializes_the_person()
    {
        var store = _fx.Services.GetRequiredService<IExternalIdentityStore>();
        store.Should().BeOfType<IdentityExternalIdentityStore>();

        await store.Link(Login("grace@example.com", "discord",
            "{\"name\":\"Grace\",\"email\":\"grace@example.com\",\"email_verified\":true}"));

        var id = await PersonOf("grace@example.com", "discord");
        var links = await store.GetByUser(id);
        links.Should().ContainSingle();
        links[0].Provider.Should().Be("discord");

        var person = await Identity.Get(id);
        person.Should().NotBeNull("the already-wired AuthSchemeSeeder Link() path must materialize the durable person");
        person!.DisplayName.Should().Be("Grace");
        (await IdentityEmail.Query(e => e.IdentityId == id)).Should().ContainSingle(e => e.Address == "grace@example.com" && e.Verified);
    }

    [Fact]
    public async Task Explicit_link_resolves_a_second_provider_to_the_canonical_person()
    {
        // Person S exists (first provider). A signed-in S EXPLICITLY binds a SECOND provider's login (microsoft, sub-t).
        var s = await Reconciler.ReconcileAsync(new IdentityClaims("person-s", DisplayName: "S", Provider: "google"));
        (await _fx.Services.GetRequiredService<IdentityLinkService>().Bind(s.Id, "microsoft", "sub-t")).Should().Be(BindOutcome.Bound);

        // Sub-t signing in via microsoft now resolves to the canonical person S — no duplicate, never via an email claim.
        var person = await Reconciler.ReconcileAsync(new IdentityClaims("sub-t", DisplayName: "T via MS", Provider: "microsoft"));
        person.Id.Should().Be(s.Id, "a bound login resolves to its person");
        (await ExternalIdentityLink.Query(l => l.IdentityId == s.Id)).Should().HaveCount(2);
    }

    [Fact]
    public async Task No_email_auto_merge_two_idps_with_the_same_email_stay_separate()
    {
        // Same VERIFIED email from two providers does NOT merge — linking is explicit only (account-takeover-safe).
        var first = await Reconciler.ReconcileAsync(new IdentityClaims("idp-one", Email: "twins@x.com", EmailVerified: true, Provider: "google"));
        var second = await Reconciler.ReconcileAsync(new IdentityClaims("idp-two", Email: "twins@x.com", EmailVerified: true, Provider: "microsoft"));

        second.Id.Should().NotBe(first.Id, "a matching verified email never auto-merges");
        (await Identity.Get(first.Id)).Should().NotBeNull();
        (await Identity.Get(second.Id)).Should().NotBeNull("both stay separate until the person explicitly binds one to the other");
    }

    [Fact]
    public async Task Sign_in_of_a_linked_provider_rewrites_the_cookie_subject_to_canonical()
    {
        var canon = await Reconciler.ReconcileAsync(new IdentityClaims("canon-person", DisplayName: "Canon", Provider: "google"));
        await _fx.Services.GetRequiredService<IdentityLinkService>().Bind(canon.Id, "microsoft", "linked-sub");

        using var scope = _fx.Services.CreateScope();
        var ci = new ClaimsIdentity(authenticationType: "test");
        ci.AddClaim(new Claim(ClaimTypes.NameIdentifier, "linked-sub"));
        var ctx = new AuthSignInContext
        {
            Provider = "microsoft",
            Identity = ci,
            Services = scope.ServiceProvider,
            HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider },
        };
        await scope.ServiceProvider.GetRequiredService<AuthFlowDispatcher>().DispatchSignIn(ctx, default);

        ci.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be(canon.Id, "the bound login's sign-in resolves to its person (NameIdentifier rewritten)");
    }

    [Fact]
    public async Task Unbind_is_owner_scoped_and_never_takes_the_last_login()
    {
        var links = _fx.Services.GetRequiredService<IdentityLinkService>();
        var owner = await Reconciler.ReconcileAsync(new IdentityClaims("owned-first", Provider: "google"));
        var first = (await links.ForPerson(owner.Id)).Should().ContainSingle().Subject;

        (await links.Unbind(owner.Id, first.Id)).Should().Be(UnbindOutcome.LastLogin, "a person signs in with a login");
        (await links.Bind(owner.Id, "discord", "owned-sub")).Should().Be(BindOutcome.Bound);
        (await links.Unbind("someone-else", first.Id)).Should().Be(UnbindOutcome.NotFound, "you cannot unbind another person's login");
        (await links.Unbind(owner.Id, first.Id)).Should().Be(UnbindOutcome.Unbound, "the owner unbinds their own while another remains");

        var arrives = await Reconciler.ReconcileAsync(new IdentityClaims("owned-first", Provider: "google"));
        arrives.Id.Should().NotBe(owner.Id, "the unbound subject's next sign-in arrives as a new person");
        (await Reconciler.ReconcileAsync(new IdentityClaims("owned-sub", Provider: "discord"))).Id.Should().Be(owner.Id);
    }

    [Fact]
    public async Task Invalid_identity_configuration_refuses_to_boot_with_its_path_and_the_fact_keeps_its_fixture()
    {
        var act = async () =>
        {
            await using var host = KoanIntegrationHost.Configure()
                .WithEnvironment("Test")
                .WithSetting("Koan:Identity:AuditSnapshotMode", "Typo")
                .ConfigureServices(s => s.AddKoan())
                .Build();

            using var hostScope = AppHost.PushScope(host.Services);
            await host.StartAsync(TestContext.Current.CancellationToken);
        };

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Koan:Identity*AuditSnapshotMode*");

        AppHost.Current.Should().BeSameAs(_fx.Services,
            "nested host cleanup must restore the fixture selected for this fact flow");
        var probeId = $"post-failed-host-{Guid.CreateVersion7():N}";
        await new Identity { Id = probeId, DisplayName = "Fixture Still Selected" }.Save();
        (await Identity.Get(probeId)).Should().NotBeNull(
            "the surrounding fact scope must keep selecting the shared fixture after nested host cleanup");
    }

    [Fact]
    public async Task An_operator_impersonating_a_person_binds_no_login_to_them()
    {
        var store = _fx.Services.GetRequiredService<IExternalIdentityStore>();
        var target = await Reconciler.ReconcileAsync(new IdentityClaims("impersonated-target", Provider: "google"));
        var acting = new ClaimsPrincipal(Koan.Identity.Impersonation.ImpersonationClaims.BuildImpersonatedIdentity(target.Id, "operator-person"));

        (await store.Bind(acting, Login("operators-own", "discord", "{}"))).Should().Be(BindOutcome.NotThePerson, "SEC-0007 D8");

        (await _fx.Services.GetRequiredService<IdentityLinkService>().ForPerson(target.Id)).Select(l => l.Provider).Should().Equal("google");
        var self = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, target.Id)], "cookie"));
        (await store.Bind(self, Login("targets-own", "discord", "{}"))).Should().Be(BindOutcome.Bound);
    }

    private static ExternalIdentity Login(string subject, string provider, string claimsJson) => new()
    {
        Subject = subject,
        Provider = provider,
        ProviderKeyHash = IdentityReconciler.ProviderKeyHash(subject),
        ClaimsJson = claimsJson,
    };

    /// <summary>The person the login (<paramref name="provider"/>, <paramref name="subject"/>) is bound to.</summary>
    private static async Task<string> PersonOf(string subject, string provider)
        => (await ExternalIdentityLink.Get(ExternalIdentityLink.KeyFor(subject, provider, IdentityReconciler.ProviderKeyHash(subject))))!.IdentityId;
}
