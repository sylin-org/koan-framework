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

# Koan Authentication & Identity: From "Already Signed In" to the Fleet

This guide walks you through Koan's identity story—from the moment you add the package and discover you're *already* authenticated, to roles, real logins, service-to-service tokens, and what changes in production. Think of it as a conversation with a colleague who's wired this up a few times: we'll start with the simplest thing that works and add one idea at a time.

A note on scope. This guide is about **identity**—*who is making this request?* Its sibling, the [Authorization How-To](authorization-howto.md), is about **authorization**—*what may they do?* We'll hand off cleanly when we get there. For the full provider/OAuth/SAML configuration reference, see [Authentication Setup](authentication-setup.md).

---

## 0. The thirty-second version: you're already signed in

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
{ "isAuthenticated": true, "id": "dev", "roles": ["admin"] }
```

You never logged in—yet you're authenticated. That's Koan's **zero-config dev identity**: in Development, an otherwise-unauthenticated request is filled in with a development principal, so `[Authorize]` and `Identity.Current` work from your very first run.

> **Mentor note.** The most common reason auth feels heavy is that you have to set it *all* up before you can test *anything*. Koan flips that: the instant you reference the package, there's an identity to build against. And it's safe—this convenience is loud in Development and **gone in Production** (we'll get there in §8).

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

Whether the caller arrived with a browser cookie, a bearer token, or the dev identity, they all land in the same place. You read identity one way.

---

## 2. Protecting an endpoint

Identity earns its keep the moment you guard something. Use the standard ASP.NET attribute:

```csharp
[Authorize]
[HttpGet("/orders")]
public IActionResult MyOrders() => Ok(/* ... */);
```

In Development the dev identity satisfies `[Authorize]`, so you can build the protected feature immediately. An anonymous caller (next section) is challenged.

---

## 3. Testing as different people — personas

Here's the part that makes the dev identity genuinely useful: you can step into *any* persona, per request, with no accounts and no login flow.

| Request | You are… |
|---|---|
| `GET /me` | the default dev user (`dev`, role `admin`) |
| `GET /me?_as=alice` | `alice` |
| `GET /me?_as=alice&_roles=editor,reviewer` | `alice` with roles `editor`, `reviewer` |
| `GET /me?_as=anonymous` | **unauthenticated** — for testing your 401 paths |

> **Mentor note.** This is how you exercise authorization without standing up real users. Want to prove an editor can't approve their own draft? Send the request `?_as=author&_roles=author` and assert the 403. Personas turn "auth testing" into ordinary request testing.

---

## 4. Roles — the coarse layer

Role checks are the first rung of authorization, and they read naturally:

```csharp
[Authorize(Roles = "admin")]
[HttpGet("/admin/reports")]
public IActionResult Reports() => Ok(/* ... */);
```

Prove both directions with personas:

- `GET /admin/reports` → the dev identity is `admin` → **200**
- `GET /admin/reports?_as=bob&_roles=viewer` → authenticated, but not admin → **denied**

Roles are deliberately coarse—they belong *in the credential* (the cookie or token). When you need decisions that depend on the specific resource, the specific action, or external policy, that's §7.

---

## 5. Real logins — OAuth providers (and the dev TestProvider)

The dev identity is for *building*. Real users sign in through a provider, and in Koan that's configuration, not code.

**OAuth (Google, Microsoft, GitHub, …):**

```json
{
  "Koan": { "Web": { "Auth": { "Providers": {
    "google": { "ClientId": "{GOOGLE_CLIENT_ID}", "ClientSecret": "{GOOGLE_CLIENT_SECRET}" }
  } } } }
}
```

That's it—`/.well-known/auth/providers` now lists Google, the challenge/callback flow runs, and a cookie session is established. (Multiple providers, SAML, and account linking are covered in [Authentication Setup](authentication-setup.md).)

**Want a real login *screen* in dev** without standing up OAuth? Opt into the **TestProvider**—a built-in fake identity provider with its own login page and token endpoint:

```json
{ "Koan": { "Web": { "Auth": { "TestProvider": { "Enabled": true } } } } }
```

> **Mentor note.** Two dev conveniences, two jobs. The **dev identity** (§0) is the zero-friction default—no screen, you're just signed in. The **TestProvider** is when you specifically want to walk the real login → cookie flow in dev (e.g. to test your callback handling). As of v0.7.0 the TestProvider is **opt-in**: it no longer auto-enables just because you're in Development.

---

## 6. Service-to-service — bearer tokens (KSVID)

Browsers carry cookies; services carry **bearer tokens**. Koan validates inbound bearer tokens through a dedicated scheme, `Koan.bearer`. Opt an endpoint into it explicitly—so a service route requires a real token, regardless of any browser session:

```csharp
[Authorize(AuthenticationSchemes = "Koan.bearer")]
[HttpGet("/internal/sync")]
public IActionResult Sync() => Ok(/* ... */);
```

A caller presents `Authorization: Bearer <token>`; a missing or invalid token is **401**. In Development, Koan both mints and validates these with a per-process key, so local service-to-service auth works with no setup. When you need to mint one yourself—in a test, or a dev tool—use the issuer:

```csharp
public sealed class SyncTrigger(IIssuer issuer)
{
    public string TokenForBilling() =>
        issuer.Issue(new TrustClaims { Subject = "billing-svc", Roles = new[] { "service" } });
}
```

> **Mentor note.** This is the seed of *fleet identity*: one verifiable token a service presents to any other, validated the same way everywhere. Today's dev keys are **ephemeral** (per process)—fine for local work. Making tokens trusted *across* machines (stable, shared, and federated keys; enrollment) is the trust-fabric roadmap (decision record SEC-0001). The inbound contract—`Koan.bearer`, `Identity.Current`, `[Authorize]`—won't change as that lands.

---

## 7. Fine-grained authorization — the handoff

Roles answer "are you an admin?" Real systems eventually need "may *this* subject perform *this* action on *this* resource?"—soft-delete this record, approve that draft, query an external policy engine.

That's a seam of its own: `IAuthorize` and its capability-graded provider ladder. Rather than repeat it here, follow the sibling guide:

→ **[Authorization How-To](authorization-howto.md)**

The one-line bridge: **identity** (this guide) establishes *who you are*; **authorization** (that guide) decides *what you may do*. They meet at `Identity.Current`.

---

## 8. Going to production — fail-closed by construction

Two things change the moment the environment isn't Development, and you don't have to remember to do either:

1. **The zero-config dev identity disappears.** No auto-sign-in. Real users authenticate through your providers; services through `Koan.bearer`. An unauthenticated request *stays* unauthenticated—`[Authorize]` means it.
2. **The ephemeral dev issuer refuses to run.** Rather than silently signing tokens with throwaway, per-process keys in production, Koan **fails the boot** unless you've configured a real issuer (or explicitly acknowledged the risk). You wire real keys/providers before you ship—there's no quiet fallback.

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
