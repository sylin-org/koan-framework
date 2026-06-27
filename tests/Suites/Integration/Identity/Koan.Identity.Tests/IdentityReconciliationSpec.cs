using System.Security.Claims;
using AwesomeAssertions;
using Koan.Core;
using Koan.Data.Core;
using Koan.Identity.Infrastructure;
using Koan.Identity.Initialization;
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
/// SEC-0007 P0 / Layer-0 acceptance (ARCH-0079 — real <c>AddKoan()</c>, offline). Proves the durable person
/// core + the wired sign-in reconciliation seams (the auth flow handler and the external-identity store), the
/// <c>Membership.IdentityId</c> soft-FK resolution, the durable dev-seed, the ambient-exempt global plane, and
/// the dev-open / prod-closed fail-closed boot guard.
/// </summary>
[Collection("identity")]
public sealed class IdentityReconciliationSpec
{
    private readonly IdentityHostFixture _fx;
    public IdentityReconciliationSpec(IdentityHostFixture fx) => _fx = fx;

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
    [InlineData(true, null, "Open")]
    [InlineData(false, null, "Closed")]
    [InlineData(false, "Open", "Open")]
    [InlineData(true, "Closed", "Closed")]
    public void Posture_resolves_dev_open_prod_closed(bool isDevelopment, string? over, string expected)
        => IdentityPostureResolver.Resolve(isDevelopment, over).ToString().Should().Be(expected);

    [Fact]
    public async Task Reconcile_upserts_durable_person_with_verified_primary_email()
    {
        var person = await Reconciler.ReconcileAsync(new IdentityClaims(
            "carol@example.com", DisplayName: "Carol", Picture: "https://x/c.png",
            Email: "Carol@Example.com", EmailVerified: true, Provider: "google"));

        person.Id.Should().Be("carol@example.com");

        var fetched = await Identity.Get("carol@example.com");
        fetched.Should().NotBeNull();
        fetched!.DisplayName.Should().Be("Carol");
        fetched.Picture.Should().Be("https://x/c.png");
        fetched.Status.Should().Be(IdentityStatus.Active);
        fetched.CreatedAt.Should().NotBe(default);

        var emails = await IdentityEmail.Query(e => e.IdentityId == "carol@example.com");
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
    public async Task Dev_seed_persists_durable_offline_dev_users()
    {
        // The Development generic-host boot trips ASP.NET's web-only DI validation, so the seed LOGIC (its durable
        // effect) is exercised directly; the dev-open gating is covered by the posture + fail-closed facts.
        await SecIdentityModule.SeedDevUsersAsync(Reconciler, IdentityHostFixture.DevUser);

        (await Identity.Get(IdentityHostFixture.DevUser)).Should().NotBeNull("the dev-seed persists a durable dev person");
        (await Identity.Get("alice@example.com")).Should().NotBeNull();
        (await Identity.Get("bob@example.com")).Should().NotBeNull();

        var aliceEmails = await IdentityEmail.Query(e => e.IdentityId == "alice@example.com");
        aliceEmails.Should().Contain(e => e.Address == "alice@example.com" && e.Verified && e.Primary);
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

        var ci = new ClaimsIdentity(authenticationType: "test");
        ci.AddClaim(new Claim(ClaimTypes.NameIdentifier, "frank@example.com"));
        ci.AddClaim(new Claim(ClaimTypes.Name, "Frank"));
        ci.AddClaim(new Claim(ClaimTypes.Email, "frank@example.com"));
        ci.AddClaim(new Claim("email_verified", "true"));
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var ctx = new AuthSignInContext { Provider = "test", Identity = ci, Services = scope.ServiceProvider, HttpContext = http };

        // Route through the REAL dispatcher (Priority-ordered, membership-checked) — the same call the cookie
        // OnSigningIn event makes — not a direct handler invocation that would bypass ordering + registration.
        await scope.ServiceProvider.GetRequiredService<AuthFlowDispatcher>().DispatchSignIn(ctx, default);

        var person = await Identity.Get("frank@example.com");
        person.Should().NotBeNull("the wired cookie sign-in dispatch must materialize the durable person");
        person!.DisplayName.Should().Be("Frank");
    }

    [Fact]
    public async Task Relink_same_provider_identity_upserts_one_row()
    {
        var store = _fx.Services.GetRequiredService<IExternalIdentityStore>();
        const string keyHash = "REKEY1";
        await store.Link(new ExternalIdentity { UserId = "heidi@example.com", Provider = "google", ProviderKeyHash = keyHash, ClaimsJson = "{\"name\":\"Heidi\"}" });
        await store.Link(new ExternalIdentity { UserId = "heidi@example.com", Provider = "google", ProviderKeyHash = keyHash, ClaimsJson = "{\"name\":\"Heidi\",\"avatar\":\"https://x/h.png\"}" });

        var links = await store.GetByUser("heidi@example.com");
        links.Should().ContainSingle("the deterministic KeyFor id must upsert the same triple, not append a duplicate");
        links[0].ClaimsJson.Should().Contain("avatar", "the surviving row reflects the latest link");
    }

    [Theory]
    [InlineData(true, IdentityPosture.Open, true, true)]      // dev + open + enabled → seed
    [InlineData(false, IdentityPosture.Open, true, false)]    // not Development → never seed a backdoor identity
    [InlineData(true, IdentityPosture.Closed, true, false)]   // Development but closed → no seed
    [InlineData(true, IdentityPosture.Open, false, false)]    // seeding disabled → no seed
    public void Seed_gate_is_dev_open_enabled_only(bool isDev, IdentityPosture posture, bool enabled, bool expected)
        => SecIdentityModule.ShouldSeedDevUsers(isDev, posture, enabled).Should().Be(expected);

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

        const string keyHash = "ABC123";
        await store.Link(new ExternalIdentity
        {
            UserId = "grace@example.com",
            Provider = "discord",
            ProviderKeyHash = keyHash,
            ClaimsJson = "{\"name\":\"Grace\",\"email\":\"grace@example.com\",\"email_verified\":true}",
        });

        var links = await store.GetByUser("grace@example.com");
        links.Should().ContainSingle();
        links[0].Provider.Should().Be("discord");

        var person = await Identity.Get("grace@example.com");
        person.Should().NotBeNull("the already-wired AuthSchemeSeeder Link() path must materialize the durable person");
        person!.DisplayName.Should().Be("Grace");

        await store.Unlink("grace@example.com", "discord", keyHash);
        (await store.GetByUser("grace@example.com")).Should().BeEmpty();
    }

    [Fact]
    public async Task Verified_email_merges_a_second_idp_onto_the_existing_person()
    {
        await Reconciler.ReconcileAsync(new IdentityClaims("google-merge", DisplayName: "Mergie", Email: "mergie@x.com", EmailVerified: true));
        (await Identity.Get("google-merge")).Should().NotBeNull();

        // A DIFFERENT IdP sub, same VERIFIED email → merges onto the existing person (differentiator ①: person ≠ email).
        var person = await Reconciler.ReconcileAsync(new IdentityClaims("microsoft-merge", DisplayName: "Mergie MS", Email: "mergie@x.com", EmailVerified: true));
        person.Id.Should().Be("google-merge", "the second IdP links onto the existing person");
        (await Identity.Get("microsoft-merge")).Should().BeNull("no duplicate identity is minted for the second IdP — the root-rot fix");
    }

    [Fact]
    public async Task Unverified_email_does_not_merge()
    {
        await Reconciler.ReconcileAsync(new IdentityClaims("anchor-sub", Email: "shared@x.com", EmailVerified: true));

        // An UNVERIFIED claim of the same email must NOT hijack the person (safe-by-default).
        var person = await Reconciler.ReconcileAsync(new IdentityClaims("attacker-sub", Email: "shared@x.com", EmailVerified: false));
        person.Id.Should().Be("attacker-sub", "only a verified email merges");
        (await Identity.Get("attacker-sub")).Should().NotBeNull("the unverified sign-in gets its own separate identity, not the anchor's");
    }

    [Fact]
    public async Task Auto_merge_is_off_by_default()
    {
        await Reconciler.ReconcileAsync(new IdentityClaims("default-anchor", Email: "default@x.com", EmailVerified: true));

        // A reconciler WITHOUT the explicit opt-in must never merge — the safe production default.
        var off = new IdentityReconciler(); // no options → AutoMergeVerifiedEmail = false
        var person = await off.ReconcileAsync(new IdentityClaims("default-second", Email: "default@x.com", EmailVerified: true));
        person.Id.Should().Be("default-second", "without the explicit opt-in, a matching verified email does NOT merge");
        (await Identity.Get("default-second")).Should().NotBeNull("a separate identity is created when merge is off");
    }

    [Fact]
    public async Task Merge_never_targets_a_deactivated_person()
    {
        var dead = await Reconciler.ReconcileAsync(new IdentityClaims("dead-anchor", Email: "dead@x.com", EmailVerified: true));
        dead.Status = IdentityStatus.Deactivated;
        await dead.Save();

        // A new IdP with the deactivated person's verified email must NOT resurrect the account.
        var person = await Reconciler.ReconcileAsync(new IdentityClaims("resurrector", Email: "dead@x.com", EmailVerified: true));
        person.Id.Should().Be("resurrector", "merging onto a deactivated person would reverse deprovisioning — refused");
    }

    [Fact]
    public async Task Ambiguous_verified_email_refuses_to_merge()
    {
        // Two distinct persons holding the same VERIFIED address (data inconsistency) → refuse to guess a target.
        await new Identity { Id = "amb-a", DisplayName = "A" }.Save();
        await new IdentityEmail { Id = IdentityEmail.KeyFor("amb-a", "amb@x.com"), IdentityId = "amb-a", Address = "amb@x.com", Verified = true, Primary = true }.Save();
        await new Identity { Id = "amb-b", DisplayName = "B" }.Save();
        await new IdentityEmail { Id = IdentityEmail.KeyFor("amb-b", "amb@x.com"), IdentityId = "amb-b", Address = "amb@x.com", Verified = true, Primary = true }.Save();

        var person = await Reconciler.ReconcileAsync(new IdentityClaims("amb-new", Email: "amb@x.com", EmailVerified: true));
        person.Id.Should().Be("amb-new", "an ambiguous match (>1 verified holder) refuses to auto-merge rather than guess");
    }

    [Fact]
    public async Task Merge_rewrites_the_cookie_subject_and_links_to_the_canonical_person()
    {
        await Reconciler.ReconcileAsync(new IdentityClaims("canon-sub", DisplayName: "Canon", Email: "canon@x.com", EmailVerified: true));

        using var scope = _fx.Services.CreateScope();

        // A second IdP signs in with the same verified email → the handler rewrites NameIdentifier to the canonical id.
        var handler = scope.ServiceProvider.GetServices<IKoanAuthFlowHandler>().OfType<IdentityAuthFlowHandler>().Single();
        var ci = new ClaimsIdentity(authenticationType: "test");
        ci.AddClaim(new Claim(ClaimTypes.NameIdentifier, "second-idp-sub"));
        ci.AddClaim(new Claim(ClaimTypes.Email, "canon@x.com"));
        ci.AddClaim(new Claim("email_verified", "true"));
        var ctx = new AuthSignInContext
        {
            Provider = "test",
            Identity = ci,
            Services = scope.ServiceProvider,
            HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider },
        };
        await scope.ServiceProvider.GetRequiredService<AuthFlowDispatcher>().DispatchSignIn(ctx, default);

        ci.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be("canon-sub", "the cookie subject is rewritten to the canonical person");

        // The external-identity store links a third IdP onto the canonical person, not a new one.
        var store = _fx.Services.GetRequiredService<IExternalIdentityStore>();
        await store.Link(new ExternalIdentity { UserId = "third-idp-sub", Provider = "discord", ProviderKeyHash = "H3", ClaimsJson = "{\"email\":\"canon@x.com\",\"email_verified\":true}" });
        (await store.GetByUser("canon-sub")).Should().Contain(l => l.Provider == "discord", "the link attaches to the canonical person");
    }

    [Fact]
    public async Task Forced_open_outside_development_refuses_to_boot()
    {
        // A non-Development host with the Identity posture forced Open must fail the SEC-0007 boot guard
        // (Open auto-seeds dev users and is legal only in Development). Trust/Tenancy do not throw in "Test".
        var act = async () =>
        {
            await using var host = await KoanIntegrationHost.Configure()
                .WithEnvironment("Test")
                .WithSetting("Koan:Orchestration:EnableSelfOrchestration", "false")
                .WithSetting("Koan:Identity:Posture", "Open")
                .ConfigureServices(s => s.AddKoan())
                .StartAsync();
        };

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Open*Development*");
    }
}
