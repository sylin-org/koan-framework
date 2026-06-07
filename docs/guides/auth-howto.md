---
type: GUIDE
domain: web
title: "Authentication & Identity How-To"
audience: [developers, architects, security-engineers]
status: current
last_updated: 2026-06-07
framework_version: v0.7.0
validation:
  date_last_tested: 2026-06-07
  status: verified
  scope: Auth fabric covered by Koan.Security.Trust.IntegrationTests (HTTP e2e)
related_guides:
  - authentication-setup.md
  - authorization-howto.md
  - building-apis.md
---

# Koan Authentication & Identity: From Public to the Fleet

This guide walks you through Koan's identity story—from a public-by-default app, to logging in as any profile you like in dev, to roles, real logins, service-to-service tokens, and what changes in production. Think of it as a conversation with a colleague who's wired this up a few times: we'll start with the simplest thing that works and add one idea at a time.

A note on scope. This guide is about **identity**—*who is making this request?* Its sibling, the [Authorization How-To](authorization-howto.md), is about **authorization**—*what may they do?* We'll hand off cleanly when we get there. For the full provider/OAuth/SAML configuration reference, see [Authentication Setup](authentication-setup.md).

---

## 0. The thirty-second version: public by default, log in when you want a profile

Reference = intent. Add the auth package:

```xml
<PackageReference Include="Koan.Web.Auth" Version="0.7.0" />
```

Boot the runtime as usual—no auth configuration:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
app.Run();
```

Now add a tiny endpoint that reports who you are:

```csharp
[ApiController]
public sealed class MeController : ControllerBase
{
    [HttpGet("/me")]
    public IActionResult Me() => Ok(new
    {
        Identity.Current.IsAuthenticated,
        Identity.Current.Id,
        Identity.Current.Roles,
    });
}
```

Run it in Development and `GET /me`:

```json
{ "isAuthenticated": false, "id": null, "roles": [] }
```

You're **anonymous** — and that's the point. Your app renders its **public** interface, which is what you want to evaluate the vast majority of the time. Nothing is auto-signed-in.

To become someone, you **log in** — and in Development that's zero-config too. Koan ships a built-in **test login page** (the TestProvider):

- `GET /.well-known/auth/providers` lists **Test (Local)** in Development;
- send the browser there, pick it, and you land on a page where you set your **subject, roles, and claims**;
- submit, and you're redirected back with a **real signed session** — now `/me` reflects that profile.

> **Mentor note.** A framework that auto-signs you in as an admin is convenient for a toy and wrong for a real app: real UX changes by profile, and you almost always want the *public* view first. So the default is anonymous, and *logging in* is an explicit act that picks a profile and walks the real session path — no auth bug can hide behind a synthetic admin. (And it's fail-closed: the dev login is **gone in production**, §8.)

---

## 1. Who am I? `Identity.Current`

`Identity.Current` is the ambient identity for the current request—available in a controller, a service, or a background job, with no `HttpContext` plumbing:

```csharp
if (!Identity.Current.IsAuthenticated)
    return; // anonymous

var who   = Identity.Current.Id;        // the subject (e.g. "dev", "alice", a service name)
var roles = Identity.Current.Roles;     // the roles carried on the credential
if (Identity.Current.Is("admin")) { /* ... */ }
```

Whether the caller arrived with a browser cookie, a service bearer token, or a `?_as=` persona, they all land in the same place. You read identity one way.

---

## 2. Protecting an endpoint

Identity earns its keep the moment you guard something. Use the standard ASP.NET attribute:

```csharp
[Authorize]
[HttpGet("/orders")]
public IActionResult MyOrders() => Ok(/* ... */);
```

By default a request is anonymous, so `[Authorize]` denies it until you log in (§0) or step into a persona (§3) — the same behaviour you'll see in production, with no synthetic admin papering over it. What "denied" looks like over HTTP is content-negotiated:

> **Mentor note.** What does "challenged" look like over HTTP? Koan's cookie scheme is **content-negotiated**.
> A browser navigation is redirected to your login page (302), but an **API request** — one sending
> `Accept: application/json`, an `X-Requested-With: XMLHttpRequest` header, or hitting a path under `/api` —
> gets a clean **401** (or **403** when authenticated-but-unauthorized) instead of a redirect, so a SPA `fetch`
> has something it can act on. Tune the heuristic (extra API path prefixes, etc.) under `Koan:Web:Auth:Challenge`.

---

## 3. Stepping into a persona — the `?_as=` quick override

The login page (§0) is how a *person* signs in. For **scripted / automated** testing there's a faster path: `?_as=` puts you into a **transient** persona for a single request — no page, no cookie.

| Request | You are… |
|---|---|
| `GET /me` | **anonymous** (the default) |
| `GET /me?_as=alice` | `alice` (role `admin` by default) |
| `GET /me?_as=alice&_roles=editor,reviewer` | `alice` with roles `editor`, `reviewer` |
| `GET /me?_as=anonymous` | anonymous (explicit) |

> **Mentor note.** This is how you exercise authorization across users without standing up accounts. Want to prove an editor can't approve their own draft? Drive the request `?_as=author&_roles=author` and assert the 403. It's Development-only and transient — perfect for an integration test that sweeps several profiles. (For a *real* clickable session, use the login page in §0.)

---

## 4. Roles — the coarse layer

Role checks are the first rung of authorization, and they read naturally:

```csharp
[Authorize(Roles = "admin")]
[HttpGet("/admin/reports")]
public IActionResult Reports() => Ok(/* ... */);
```

Prove both directions with personas:

- `GET /admin/reports?_as=alice&_roles=admin` → admin → **200**
- `GET /admin/reports?_as=bob&_roles=viewer` → authenticated, but not admin → **denied**

Roles are deliberately coarse—they belong *in the credential* (the cookie or token). When you need decisions that depend on the specific resource, the specific action, or external policy, that's §7.

---

## 5. Real logins — OAuth providers (and the dev TestProvider)

Your dev login (§0) is the built-in TestProvider. Real *users* sign in through a real provider — and in Koan that's configuration, not code.

**OAuth (Google, Microsoft, GitHub, …):**

```json
{
  "Koan": { "Web": { "Auth": { "Providers": {
    "google": { "ClientId": "{GOOGLE_CLIENT_ID}", "ClientSecret": "{GOOGLE_CLIENT_SECRET}" }
  } } } }
}
```

That's it—`/.well-known/auth/providers` now lists Google, the challenge/callback flow runs, and a cookie session is established. (Multiple providers, SAML, and account linking are covered in [Authentication Setup](authentication-setup.md).)

The **TestProvider** you logged in with at §0 is exactly this: a built-in fake identity provider with its own login page and token endpoint. It's available **zero-config in Development**; to expose it anywhere else (a shared demo, say) opt in explicitly:

```json
{ "Koan": { "Web": { "Auth": { "TestProvider": { "Enabled": true } } } } }
```

> **Mentor note.** Two dev paths, two jobs. The **TestProvider login** (§0) walks the real challenge → callback → cookie flow and mints a real session — use it to build and test the actual login experience, set roles/claims, and see the profile-specific UX. The **`?_as=` override** (§3) is the transient, no-click path for scripted tests. Neither exists in production.

---

## 6. Service-to-service — bearer tokens (KSVID)

Browsers carry cookies; services carry **bearer tokens**. Koan validates inbound bearer tokens through a dedicated scheme, `Koan.bearer`. Opt an endpoint into it explicitly—so a service route requires a real token, regardless of any browser session:

```csharp
[Authorize(AuthenticationSchemes = "Koan.bearer")]
[HttpGet("/internal/sync")]
public IActionResult Sync() => Ok(/* ... */);
```

A caller presents `Authorization: Bearer <token>`; a missing or invalid token is **401**. Koan signs and validates these with a **shared secret**, `Koan:Security:Trust:Key`, which in a fresh app **defaults to a well-known development value**. Because every Koan service defaults to the *same* key, they all **self-mint valid tokens and trust each other with zero configuration** — local service-to-service auth just works. To mint one yourself — in a test, a dev tool, or a service calling another — use the issuer:

```csharp
public sealed class SyncTrigger(IIssuer issuer)
{
    public string TokenForBilling() =>
        issuer.Issue(new TrustClaims { Subject = "billing-svc", Roles = new[] { "service" } });
}
```

> **Mentor note.** This is the seed of *fleet identity*: one verifiable token a service presents to any other, validated the same way everywhere. That default key is **loudly insecure by name** (`super-insecure-shared-secret-replace-asap`) and bootstrap warns you on every start — it's for local dev only, and Koan **refuses to boot** a Production/Staging app that still uses it (§8). For a private team or a shared environment, set `Koan:Security:Trust:Key` to a real secret; every service that shares it interoperates. (Per-node asymmetric identity with *no* shared secret is the SEC-0001 fleet roadmap — the `Koan.bearer` / `Identity.Current` / `[Authorize]` surface won't change as it lands.)

---

## 7. Fine-grained authorization — the handoff

Roles answer "are you an admin?" Real systems eventually need "may *this* subject perform *this* action on *this* resource?"—soft-delete this record, approve that draft, query an external policy engine.

That's a seam of its own: `IAuthorize` and its capability-graded provider ladder. Rather than repeat it here, follow the sibling guide:

→ **[Authorization How-To](authorization-howto.md)**

The one-line bridge: **identity** (this guide) establishes *who you are*; **authorization** (that guide) decides *what you may do*. They meet at `Identity.Current`.

---

## 8. Going to production — fail-closed by construction

Two things change the moment the environment isn't Development, and you don't have to remember to do either:

1. **The dev conveniences disappear.** No `?_as=` personas and no zero-config TestProvider login. Real users authenticate through your providers; services through `Koan.bearer`. An unauthenticated request *stays* unauthenticated — `[Authorize]` means it.
2. **The default insecure key is refused.** In dev, an app with no `Koan:Security:Trust:Key` runs on the public default secret — with a very loud warning banner on every boot. In **Production or Staging**, Koan **refuses to start** on that key: set a real secret first (or, for a throwaway box, `Koan:Security:Trust:AllowInsecureKeyInProduction=true`). No quiet fallback.

> **Mentor note.** The design goal is that you *cannot accidentally ship the dev conveniences*. They're loud in Development and absent—by construction—in Production. "Everyone is admin" can't leak past your environment boundary.

---

## 9. Testing your auth, end-to-end

Auth bugs love the seams *between* components—exactly where unit tests can't see. Koan ships an HTTP-level pattern for this: a fixture boots your app over an in-memory server, and you drive real requests, asserting status codes. You can mint bearer tokens via the app's *own* issuer (so the scheme accepts them) and step into personas with `?_as=`:

```csharp
[Fact]
public async Task Bearer_endpoint_rejects_a_missing_token()
{
    var res = await _fixture.CreateClient().GetAsync("/internal/sync");
    res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
}

[Fact]
public async Task Admin_route_allows_the_admin_dev_identity()
{
    var res = await _fixture.CreateClient().GetAsync("/admin/reports");
    res.StatusCode.Should().Be(HttpStatusCode.OK);
}
```

A complete, runnable example lives in `tests/Suites/Security/Koan.Security.Trust.IntegrationTests`—worth reading once; it's the same harness that caught a real dev-identity wiring bug before it could reach you.

---

## Where to go next

- **What may they do?** → [Authorization How-To](authorization-howto.md)
- **Provider/OAuth/SAML reference** → [Authentication Setup](authentication-setup.md)
- **Building the API around it** → [Building APIs](building-apis.md)
