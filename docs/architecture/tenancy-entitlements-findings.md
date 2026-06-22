# Tenancy entitlements & tiers — prior-art findings (two sweeps)

- Date: 2026-06-22
- Sources: sweep #2 `wf_7c331efc-09c` (tier/quota CORE, 25 platforms) + sweep #3 `wf_9c953f4e-e6c` (entitlement DIMENSIONS beyond the core, 36 platforms) → 61 platforms total. Decisions are in **ARCH-0099 §8**; this is the durable research record.
- Question: should §7 grow a tier/plan/edition layer + entitlements, and how do tiers/quotas resolve + enforce + lifecycle?

## Verdict

**Yes — add a tier layer; it is one more resolution layer, not a subsystem.** Universal convergence: a tier/plan/edition is a named catalog object a tenant is *assigned* to, with entitlements attached (Stripe Product, Chargebee Plan, ABP Edition, Salesforce License, M365 SKU). ABP confirms it is architecturally cheap — one more value-provider in an existing chain (`Default→Configuration→Edition→Tenant`), "not a new subsystem." **§7's lockable per-tenant config primitive remains genuinely novel** (nobody ships it); the **moat** is that Koan owns *both* resolution (§7) and the enforcement chokepoint (§1b) — the billing/auth incumbents resolve plans but punt on numeric-quota enforcement.

## The load-bearing answer: numeric resolution = four declared operators

Make the operator a **declared per-grant property**, not a hardwired engine rule (Stigg's keystone):
- **REPLACE** — dominant default for an explicit per-tenant override over a tier (Lago verbatim, Chargebee `is_overridden`, ABP first-non-null-wins, all infra-grade: AWS applied-quota, GitLab `actual_limits`, K8s, Cloudflare). *Is* §7's `Override`. → §8 default.
- **MAX-with** — dominant default for combining multiple catalog sources (plan + add-ons) for the same feature; never silently revokes paid access. **Do not hardwire** — Schematic's hardwired MAX is a one-way ratchet that cannot clamp a tenant *below* their plan (abuse-throttle/contract-cap footgun).
- **ADD** — opt-in only (Stigg "Increment plan limit ×instances"). Every mature platform models additive top-ups as a *separate ledger* (Salesforce `CurrentAmountAllowed` grows by purchase; M365 `prepaidUnits`), never a magic merge on one scalar.
- **SUM/Pool** — consumable credits (Stigg pooled, Metronome/Orb burn-down, Stripe credit grants stack by priority).
- Alternative for multiple limit *sources*: **independent buckets, most-restrictive binds** (Auth0 M2M quotas, K8s `ResourceQuota`) — "REPLACE within a scope, min-across scopes"; reports *which* constraint bound.

## Entitlement typing (the gap §8 closes)

Type the entitlement — ABP's **stringly-unified value** (`FreeTextStringValueType` + numeric validator, parse-late) is the proven failure ("no add/max/grace expressiveness — the logic has nowhere structural to live"); Stripe Entitlements is **boolean-only** so quotas bolt on and drift. The good models type it: Lago `{boolean, integer, string, select}`, Chargebee `{switch, quantity, range, custom}`, Stigg `{boolean, configuration, metered, credits}`. §8 kinds: `{boolean-feature | numeric-limit | numeric-rate | metered | typed-config-value | enum-set}`. The lock reuses §7: `Locked | TenantMayChange | TenantMayChangeWithin(envelope)` — the envelope = K8s `LimitRange` (default + min/max) made numeric.

## Enforcement

- **Billing platforms resolve but never enforce** — the gate is app-side everywhere (Stripe/Chargebee/Recurly/Paddle/Lago); usage platforms (Orb/Metronome/m3ter) only meter + bill overage. **Hard real-time gating exists only in dedicated entitlement layers** (Stigg's check API + sidecar, <100ms). Koan is uniquely positioned: §7 resolution × §1b chokepoint = resolve *and* enforce.
- **GitLab `Limitable`** is the shape to steal: one reusable chokepoint keyed by `(entitlement-code, scope)`; adding a quota = a declaration. Default **hard admission-time** (reject the (N+1)th create). Soft-vs-hard is a **per-quota policy** (GitHub's "stop at budget" flag); **observe-mode** (Auth0 `enforce:false` + graduated 60/80/100% warnings) = shadow rollout; **always a hard backstop** (GitHub notify-only-default = silent-overage footgun).
- The **rejection contract**: name the entitlement, current-vs-limit, which *layer* set the ceiling, utilization%, the upgrade path (K8s 403 / AWS limit error / Auth0 `X-RateLimit-*` + `Organization-Quota-*` headers).

## Metering

- **Limit ⊕ Meter, two halves, §8 owns the join.** The declarative limit (near-static, cache hard) + a live burn-down balance (separate streaming meter). The OpenFeature negative result: a flag knows "500" but nothing counts against it — retrofitting metering onto flags is the expensive rewrite. **Don't ship numeric quotas as flags.**
- **Meter↔gate consistency** = two-tier: every meter (Stripe/Lago/OpenMeter/Metronome/Amberflo) is eventually-consistent (Lago `ongoing_balance` ~5min). Meter is source-of-truth for *billing + surfacing*; **hard caps enforce via a local transactional reserve at the chokepoint**, reconciled to the meter. Cache the limit; never cache the gate-critical balance.
- **Idempotency is domain-level** — dedup by the action's own id over a window (CloudEvents id, Lago `transaction_id`, Stripe *event* identifier — explicitly NOT the HTTP `Idempotency-Key` header). Aligns JOBS-0005 at-least-once.
- Ledger fields (Salesforce `TenantUsageEntitlement`): `Allowed/Used/Grace/Rollover/Frequency`. Surface the live number *labeled estimate* (Lago dual-balance). Point-in-time check (OpenMeter `time`) avoids double-grant/deny during tier flips.

## The over-limit / downgrade lifecycle (sweep #3's biggest find)

- **Four-response ladder:** grandfather/meter-overage · **write-freeze (read-only)** · lock (entitlement-gated, reversible) · reclaim/delete. Pick by whether a payment instrument exists (Mailchimp: monetize where a card exists, freeze where it doesn't).
- **Write-freeze is the safe default; never lock the remediation path** (the single most common bug — GitHub locked-AND-invisible, Dropbox read-only blocks the share/move to triage, Mailchimp blocks even test sends). Gate exactly the cost-*growing* op (Notion: disable new *blocks*, edit/reorganize/delete stay open; Google: block send/upload, keep read/delete/export).
- **Never hard-delete to enforce** (Dropbox-Basic is the outlier + most-complained). GitHub *rehydrates byte-for-byte* on re-upgrade; Google's grace SLA = 2-year-over-quota + 3-month notice.
- **Downgrade defers to the billing-cycle boundary** (upgrade-now/downgrade-later; prorate-up / `proration=none`-down — symmetric handling is the #1 plan-change error). A scheduled change is **durable, single-pending, must survive unrelated edits** (Stripe/Chargebee silently drop a pending change on any edit — the footgun) and renders in RSoP as a pending future-state.
- **Versioned tier definitions** so grandfathering is data; time-limited (12–24mo) + deprecation timeline; migration is a P8 saga.
- **Forward-only enforcement** (K8s/HNC/Kueue never retroactively evict) confirms: lowering a tier blocks growth, doesn't reclaim — freeze-in-place, and §3 placement reinforces it (8000 photos are expensive to relocate; the entitlement gate changes, the data stays put).

## Hierarchy, taxonomy & capacity

- **Resolution direction differs by kind** (GitHub runs both side-by-side): config/settings = deepest-wins/override (ABP `Default<Config<Global<Tenant<User`); capabilities = most-permissive/union; policy floors = most-restrictive/intersection (GitHub rulesets = §7's `TenantMayChangeWithin(envelope)`). A single universal chain is a structural mistake.
- **The user level exists only for the *soft* kinds** (config-values / per-user prefs — ABP `UserSettingValueProvider`, Slack/Notion member prefs); features/tiers stop at tenant. The `tenant-over-user` bound is the *same* §7 lock one level down (recursive descriptor).
- **Capacity ≠ quota** (AWS: "quota is permission, capacity is infrastructure — separate checks"; `InsufficientInstanceCapacity`). A tier promising §3 dedicated placement is **grant-AND-reserve** via the P6 broker, never just a number. Pooled tiers may overcommit; dedicated cannot.
- **Aggregation over `ParentTenantId`:** resources = shared-pool walk-up-most-restrictive (HNC; Kueue guarantee+borrow so no child starves a sibling); seats = **distinct-user subtree rollup** (GitHub one-user-one-license-across-orgs; never naive sum).

## Billing decoupling & product surface

- **Billing is one grantor; gate on subscription *status* not payment** (Stripe webhook → set tier; Clerk/Stigg keep the catalog native, Stripe for payment only). Grant-without-billing (comp/trial/internal) is just another *source* in the multi-source resolver. App gates on a **stable entitlement key**, never a plan id.
- **The gate is server-side; the paywall is client-side — never conflate** (Superwall/RevenueCat targeting is spoofable; "server-side for anything security-sensitive"). The §6 portal is presentation; §1b is enforcement.
- **One cascading typed-config plane absorbs branding + entitlements + quotas** — white-label (logo/theme/domain) is just presentation *keys* in the same §7 cascade (RevenueCat offering-metadata bag proves the merge). **Price preview from the billing engine, never recomputed** (Paddle `PricePreview`).
- **Templated config values are a security boundary** — tenant-editable templates use a **closed grammar / allowlisted placeholders** (data) or a sandboxed renderer (Liquid); never Handlebars/Razor (documented SSTI→RCE, Shopify Return Magic).

## The AVOID list

Hardwiring one numeric operator (Schematic) · encoding "add 500" as a magic scalar merge · storing numeric limits in the boolean-feature plane (ABP) · building entitlements on a feature-flag engine (rule spaghetti, no usage tracking) · reading the eventual meter for a hard gate · conflating the event id with the HTTP idempotency key · **hard-deleting tenant data on downgrade** · **locking the remediation path** · symmetric upgrade/downgrade handling · letting a scheduled change be silently clobbered · client-side resolution that makes the lock advisory · per-tenant containers as the default placement.
