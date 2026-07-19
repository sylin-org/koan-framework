---
type: ARCH
domain: framework
audience: [architects, maintainers]
status: archived
last_updated: 2026-07-19
framework_version: v0.20.0
validation: 2026-07-19
---

# Tenancy delight synthesis — 5-persona blind harvest

- Date: 2026-06-22
- Source: blind 5-persona delight harvest `wf_0d56fd46-7ae` (business · developer · engineer · architect · end-user → synthesis). 49 magic moments.
- Scope: the experiential payoff of the Koan tenancy (ARCH-0099 §1–§8) + classification (ARCH-0098) design, grounded in the "SnapVault" example.

## The unanimous flagship — lead with the one that's BUILT

**"The cross-tenant leak you literally cannot write"** — fail-closed isolation at one chokepoint, **proven (not promised) on every build.** All five personas independently landed here, and it is the flagship that is genuinely **built + mutation-checked** (`AssertNoTenantLeakSpec`, real `AddKoan()` boot, SQLite + Postgres/SqlServer/Mongo fan-out). The convergence is structural, not emotional: it's the *same* capability (§1b `TenantStorageGuard` + §3 write-stamp-and-verify) seen from five angles, and it's the substrate everything else (quota enforcement, classification, erasure) rides on.

- **Business:** "my biggest existential risk — one customer seeing another's photos — is the thing my framework structurally won't let me write, with a fresh proof every CI run instead of a pen-test six months too late."
- **Developer:** the exception that *refuses* a cross-tenant read and hands back the three exact fixes — "the data leak I can't ship by accident."
- **Engineer:** "I went looking for the endpoint that forgot the tenant filter and there wasn't one — the discriminator is an invisible non-POCO field filtered below the adapter." Cross-tenant *takeover* (not just exposure) is rejected.
- **Architect:** "the leak moves from a discipline everyone must remember on every query to a closure no one can bypass; the field (Rails/Finbuckle/Nile) fails OPEN exactly here, and the proof is a green mutation-checked test, not a code-review convention."
- **End user:** "another studio's photos in my gallery is the bug that ends a photography business — here it's structurally impossible, not 'we try hard'."

## Per-persona magic moment

- **Business** — the line item that disappears: "reference the module + posture-by-env" buys, day-0, what a SaaS hires a team + three vendors (Clerk/WorkOS + Stripe-entitlements + a config-service + a Skyflow-style vault) to build. The GoodRx-class ~6mo/~10-eng vault collapses into a reference; pitch "HIPAA-compatible, GDPR-erasure-provable" day one.
- **Developer** — first `dotnet run` and he's *already* Owner of a working tenant smart-named "Acme" from his git email; "the only infrastructure code I wrote was attributes," each enforced at one chokepoint, no Stripe/Clerk dashboard context-switch.
- **Engineer** — a forced-open posture or leaked `koan-dev-insecure-` key **refuses to boot** in prod and names the exact fix: "the breach I'll never get paged for."
- **Architect** — one overlay primitive subsumes tenancy isolation AND classification at ONE write-plan slot: "not three integrations, three declarations against one seam" — the surface stays FLAT as cross-cutting concerns multiply.
- **End user** — honest quota: "480/500 used, tier-1 gives 2000" — no silent counter, no `ALMOST OUT!!!` dark pattern, and the count is *real* (Koan owns the resolution plane). "Fairness is a structural property, not a marketing line." *(DESIGNED — riskiest to over-promise.)*

## The moat (why-Koan), stated identically by 4 of 5

**Koan owns BOTH the resolution plane (§7) AND the fail-closed chokepoint (§1b)** — it *resolves* the tier/quota AND *enforces* it at the same gate that already blocks cross-tenant access. It rejects the 501st upload itself. Stripe/Clerk/WorkOS can tell you a customer's plan but **structurally cannot stop the 501st upload** — they resolve and punt on numeric-quota enforcement. "The resolver and the enforcer are the same machine." Decisive *because the enforcement substrate is already built*; only the entitlement layer riding it is roadmap. (Also: erasure certificate rolls up every axis no single vendor sees across; Suspend is atomic next-request, not after a JWT TTL; pooled→silo is a placement field + saga, not a 6–12mo rewrite.)

## North-star demo (SnapVault, one sitting)

`A` clone & run, zero config → boot prints `posture=Open`, you're Owner of "Acme", upload a photo. `B` second tenant "Globex" → GET Acme's image by guessed id = not-found, overwrite Acme's row = "cross-scope write" rejected; show `dotnet test` AssertNoTenantLeak **green**. `C` tag `[Pii]`, re-save, `sqlite3 … select Json` → ciphertext at rest while the app shows plaintext in hand. `D` upload #501 → the *framework* rejects it, naming which layer set the ceiling + upgrade path. `E` "delete my account" → crypto-shred → `[Pii]` read returns null tombstone (never ciphertext) + a signed erasure certificate. `F` redeploy to Production with the dev key in config → **refuses to boot**, names the line to remove.
**Spine A·B·C·F is PROVEN today; D·E are DESIGNED — show as dated roadmap, never as shipped.**

## The honesty layer — what's BUILT vs DESIGNED (the delight depends on it)

The two highest-leverage delights are **designed, not built**, and the substrate *under* them is built:

- **Quota-enforcement moat** (every persona's competitive headline) — §8 typed entitlements + operators + metering ledger + layer-attributing rejection: **DESIGNED**. Only the chokepoint it rides is real.
- **Signed erasure certificate** (the compliance flagship) — emitter + durable-carrier classified-stripping + multi-axis fan-out: **DESIGNED**. Only crypto-shred substrate is built.
- **Self-service portal** (`Koan.Tenancy.Web`) — **does not exist** yet; the durable control-plane under it is built.
- **RSoP explainer + governed-config single-resolver** — **DESIGNED**, no resolver in `src/`. The whole §7 differentiation evaporates if the first impl admits one client-merge/raw-config path.
- **Async-hop / durable-carrier coverage** — the §1b chokepoint covers the request path, but jobs/messaging/outbox serialize OUTSIDE it; ARCH-0098 §6 names the message bus as the largest uncovered hole (a `[Phi]` caption rides it in plaintext today). **This is the gap that most threatens the flagship itself.**

## Delight-killers (what turns each delight into rage)

1. **Ship proof artifacts before the fan-out is exhaustive** (master killer, 4/5). A signed erasure cert that certifies "deleted" while a plaintext `[Phi]` sits in a DLQ is *signed false proof* — legally voids the cert. An honest "async-purging, ETA 4h" **beats** a confident lie.
2. **Marketing blurring PROVEN vs DESIGNED** — poisons the "provable not promised" positioning. Sell the proven floor as proven, the rest as a *credible dated roadmap*. Carry the no-boot-lies honesty into the copy.
3. **Asymmetric / silent-partial coverage the boot report claims is complete** — if `[Pii]` decrypts on by-id but leaks ciphertext through a `Query`, the delight inverts into betrayal worse than never claiming it. `enforce` must mean enforce *everywhere* or *loudly* name where it doesn't.
4. **A wrong/vague quota count** — read as a *moral* statement, not a metric: "480/500 but truth is 520" → "this system lies to upsell me." The ledger must be exact; the RSoP must be correct + specific.
5. **The "one resolver, one chokepoint" invariant leaking anywhere** — any surface resolving/enforcing outside the chokepoint collapses the moat into advisory-lock mediocrity. Includes async/durable paths.
6. **A stranded Relocate saga / the single-author bet** — mid-cutover stuck state; P6 pool-exhaustion/session-reset cross-contamination; "who do I call at 2am."

## Sleeper hits (underrated — elevate to first-class)

- **Graduated / observe-mode enforcement (advise→warn→enforce)** — "I dropped free from 500→400 in observe-mode, got the list of tenants already over, then migrated on a schedule instead of paging support." Same mechanism as the non-cliff retrofit; the adoption on-ramp for *every* future policy change.
- **The non-cliff retrofit** — "tenancy is a feature you add, not a rewrite you defer forever." A top-3 adoption lever buried as a minor moment.
- **Measured (not proxy-guessed) per-tenant cost / noisy-neighbor** — falls out free from the same stamping that powers isolation.
- **The "why is this Locked" explainer for END USERS** — the RSoP surfaced in the portal UI is a support-ticket-deflection + customer-trust feature, not just an operator tool.
- **Crypto-shred returns a null tombstone, never ciphertext** — already BUILT; elevate as a named guarantee (post-shred reads are honest, nothing to explain to an auditor).

## The strategic read

The harvest gives a clean **priority order**: the flagship (isolation) is *built* — lead every story with it. The two highest-ROI things to build next are the **quota enforcement** and the **erasure certificate**, because they are the most-cited delights and currently have no code. But the **async-hop / durable-carrier hole is the existential prerequisite** — it must close before "structurally unwriteable leak" can be claimed, because a quota or `[Phi]` bypassed in a background job makes the flagship itself false. And the cross-cutting discipline is Koan's own **no-boot-lies honesty applied to positioning**: never let a proof artifact (certificate, "enforce", quota count) outrun the exhaustiveness of what's behind it.
