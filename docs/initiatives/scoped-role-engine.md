---
type: SPEC
domain: identity
title: "Scoped role engine — reusable authorization for Koan applications"
audience: [maintainers, framework-authors, application-authors, ai-agents]
status: approved-for-implementation
last_updated: 2026-09-13
validation:
  status: design-reviewed
  scope: owner-approved requirements; implementation and verification are tracked below, not implied
---

# Scoped role engine

## 1. Charter and authority

Leo approved a domain-neutral, Discord-like role engine in Koan, with Tangent Space as its first
consumer. Developers define the available actions and boundaries; administrators configure access
within those boundaries; Koan resolves, explains and enforces the result consistently.

This document is the implementation specification and compact handoff. It is not a statement of
shipped capability. The [progress ledger](#12-progress-and-handoff) is the only live implementation
status here. No additional epic, planning ceremony or external authorization service is required.
Existing contributor law in [CLAUDE.md](../../CLAUDE.md) remains authoritative. Concrete package,
namespace and API names must follow the existing owners and Koan grammar; concept names below are
not preselected public API identifiers. Record material semantic changes here before implementing
them; explain deviations to the coordinating Tangent task rather than silently changing the promise.

The implementation belongs in the actual Koan repository. Tangent owns consumer adoption and its
product UI, not a duplicate engine or a final patch to an ignored framework checkout.

### Outcomes

- An application can declare capabilities and a scope hierarchy without hard-coding its role names.
- Administrators can create roles and assign multiple roles to subjects at selected scopes.
- Inheritance, local overrides, delegation and restrictions have one predictable interpretation.
- Listing, individual-resource access, mutations, explanations and previews agree under the same
  authoritative facts. Data access remains genuinely bounded.
- Humans, agents and services use the same decisions through all supported transports.
- A small second-domain fixture proves that the framework contains no Tangent-specific policy.

## 2. Verified starting point and ownership

The initial source review and Koan agent's fit assessment used revision
`30586ebf8c878fec04047aceefdad0e261c8c532`. Recheck current source before implementing.

| Existing owner | What is available at that revision | Boundary to preserve or extend |
|---|---|---|
| `Koan.Identity` | `IdentityRole`, `IdentityRoleService`, `EffectiveAccessResolver`, source-row access facts | Global identity-role bindings and explanation inputs; no operative editable scoped-role catalog |
| `Koan.Tenancy` | `Membership.Roles`, identity/tenancy contribution to role claims | Tenant-root membership; admission is not the same as a resource-scoped assignment |
| `Koan.Web.Authorization` | `IAuthorize`, `AgentGrant`, `EntityAccess<T>`, gate/constrain/row projection | Extend the effective enforcement path; do not add a bypassing role-controller path |
| `Koan.Identity.Access` | `AccessExplainer.CanAsync` and `WhyAsync` | Current forward explanation uses the coarse authorization seam, not the full row constraint; it is not sufficient as a resource-access preview |

`AgentGrant` supports independently revocable, optionally expiring direct capability grants, but its
resource is an entity name or `*`, not a nested resource-instance scope. Membership facts are not
currently handled by the explainer's revoke operation. These are reviewed capability boundaries,
not newly demonstrated security defects.

The former Web.Auth.Roles catalog was retired because its definitions were inert. Do not revive it
as a parallel catalog. Extend Identity's effective path and the existing enforcement owners.

Koan owns definitions, bindings, normalization, delegation validation, evidence and integration with
query/access enforcement. Apps own their capability vocabulary, scope ancestry, trusted audience
relationships, business guards, role defaults and presentation. Tangent's public reading, admission,
human Host ownership, agent pause and moderation rules are app contributions, not Koan constants.

## 3. Logical model

The storage and C# surface may be shaped by the existing framework; the following semantics are
required. Prefer Entity-first definitions and bindings, and normal Koan composition/storage.

| Concept | Required meaning |
|---|---|
| Subject | Stable identity independent of credential, runner, model or device; anonymous is an explicit request state |
| Capability descriptor | Stable namespaced action key, supported resource/scope types, description and optional typed conditions/parameters |
| Scope reference | Typed, tenant-bound stable key and optional trusted parent; single-parent ancestry in v1 |
| Role definition | Stable identity, owning scope, name/purpose, default conditional grants, supported applicability, status/version, optional presentation metadata |
| Role binding | Subject, role identity, scope, propagation (local or descendants), optional expiry, issuer and audit provenance |
| Action override | At a scope, either inherit or replace the ordinary audience for one action with explicit conditional alternatives |
| Delegation authority | What definitions, assignments or policy changes an actor may authorize, for which scopes and within which limits |
| Resolved decision/plan | Decision, contributing definitions/bindings/overrides, safe reason, relevant limits, evaluated versions and supported query representation |

Role identity is not its editable name. Names such as Owner or Administrator convey no intrinsic
power. Role renames, colors and order must not change authority. A role may have no default grants.
Derived groups such as workspace members, resource author or assigned reviewer are trusted app
relationships, not editable role names. Group membership and resource admission remain separate
from ordinary role assignments.

Definitions and bindings must survive persistence round-trips and be safely revocable. Unknown
capabilities, role references, scope types, invalid ancestry or unsupported policy versions must
not become permissive fallbacks. Bound ancestry and fact resolution; reject cycles and cross-tenant
edges. A request cannot choose its own trusted parent, subject attributes or restriction facts.
Disabled/retired roles and revoked/expired bindings stop contributing both default grants and
matches for role selectors inside overrides. Disabling a badge-only role must revoke access granted
through its explicit audience selection too; stale referenced definitions never mean everyone.
In v1, retirement revokes existing role-derived authority and prevents new assignments; it does not
mean merely closing a role to newcomers while preserving its existing grants.

## 4. Versioned evaluation semantics

Use one documented semantics profile. Apps customize catalogs and typed rules, not arbitrary
conflict-resolution callbacks. Freeze the v1 cases below in tests before optimizing the resolver.

### 4.1 Ordinary grants and overrides

For a subject, action and target:

1. Resolve the authenticated identity/anonymous state, trusted target scope and bounded ancestry,
   applicable live bindings, role versions, audience facts and action context.
2. Find the nearest explicit `Replace` override for that action, searching from the target scope
   toward its root. `Inherit` contributes no replacement. An explicit empty audience means nobody;
   it is not the same as inheritance.
3. If a replacement exists, its alternatives are the entire ordinary allow set for that action.
   Role default grants do not bypass it, including grants from child-local bindings. Role selectors
   in the replacement match bindings applicable at the target according to their propagation.
4. If no replacement exists, combine applicable role default grant clauses and explicitly
   integrated default audience grants. No matching clause means deny.
5. Evaluate each grant as a complete conjunction of its conditions, and combine alternatives with
   OR. Apply mandatory app guards, action prerequisites, scope/tenant boundaries and credential
   limits to the result. None of those limits is widened by an ordinary grant or replacement.

Role defaults and scope overrides are distinct editable records normalized into one effective
policy. A role editor describes default authority and exceptions; it must not present the role's
default action list as unconditional authority everywhere.

A child-local binding alone cannot silently defeat an inherited replacement. An authorized child
exception must be expressed as a child replacement, preserving the intended other audience entries.
For example, adding a private-Topic guest under a Members-only replacement requires an explicit
child audience including that guest. An invitation workflow may expose this as one understandable
operation, but it must validate the policy change as well as the binding. If the inviter lacks that
authority, refuse with a safe explanation instead of quietly broadening access.

This choice makes the UI statement "inherited: Publishers only" true until someone deliberately
customizes that action. Binding propagation and action-rule inheritance are separate controls.

### 4.2 Mandatory limits and ownership

An inherited default is not a mandatory ancestor ceiling. A private parent's ordinary policy may
be replaced by an authorized public child policy; a separately declared no-publication ceiling
cannot. Child access does not require permission to read parent metadata, and never grants sibling
or upward access. Apps must project/breadcrumb only authorized context.

Ownership is a trusted relation with explicit app-declared powers. There is no built-in omnipotent
Administrator role or owner short-circuit that skips mandatory guards. Apps may define recovery or
moderation-inspection actions separately from ordinary reading/posting. A guard declares the actions
it limits, allowing deliberate recovery paths without an accidental universal bypass.

An anonymous audience is explicit and only valid where the action descriptor permits it. Account
bans cannot promise to prevent anonymous reading of still-public content. Public/member audiences,
locks, suspensions and agent pauses are app semantics supplied through the same resolver.

### 4.3 Conditional grants preserve correlation

These two grants remain alternatives:

```text
approve(amount <= 500 AND department == A)
approve(amount <= 5000 AND department == B)
```

They must never become `approve(amount <= 5000 AND department in [A, B])`. Preserve the provenance
and complete condition bundle of every grant, including its scope, target type and expiry. Mandatory
limits intersect complete eligible paths; scalar limits must not be independently maximized.

## 5. Customization contract

### Application-author declarations

Applications can declare:

- Capability keys, applicable resource types, labels, descriptions, categories, optional risk hints,
  anonymous eligibility, delegation eligibility, prerequisites and typed parameter schemas.
- Trusted scope mappings and permitted propagation/override behavior.
- Trusted audience providers: authenticated subjects, members, role holders, explicit subjects,
  ownership/authorship or other named app relationships. Fact provenance is explicit.
- Named, deterministic typed guards/conditions and their supported query representation. Examples
  include author-only edits, lifecycle state, authentication assurance and separation of duties.
- Default roles, policy presets and presentation metadata. Extension metadata is namespaced and
  does not acquire executable authority merely by being present.

Structural declarations compile once with the Koan host shape. Dynamic role/binding/policy data
is resolved into immutable versioned request plans. Module reference and `AddKoan()` remain the
composition grammar; do not require manual provider or endpoint wiring in every consumer.

Administrators can create role names and select existing executable capabilities. Typing a new
capability key is not a way to implement behavior. New capabilities do not automatically widen old
roles through an unreviewed wildcard. Invalid prerequisites and parameter values fail correctively.

### Administrator and scoped-steward configuration

- Create, rename, disable and retire roles; edit default grants only within definition authority.
- Assign/revoke multiple roles with explicit scope, propagation and optional expiry.
- Configure per-action inheritance/replacements within policy-management authority.
- Apply understandable presets. Presets initialize policy; later template changes are explicit,
  especially when access expands. No implicit live-template inheritance in v1.
- Preview effective access and proposed changes only within the caller's preview authority.
- Mark roles protected or integration-managed when their lifecycle belongs to a trusted owner.

The framework is headless. It supplies validated descriptors and operations that a human settings
UI or an agent interface can present. A universal settings console, theme system, role icon hosting,
mention notification system and automatic agent execution are not part of this engine.

### Optional Web complement

`Koan.Identity.Web` owns the authenticated management translation for this engine. Referencing core
Identity alone does not mount management routes; referencing the Web complement activates a distinct
tenant-and-scope-bound route group through normal `AddKoan()` composition. Route exposure never
grants authority, including in Development. This surface does not reuse the existing global
identity-operator controller: a delegated scope steward neither needs nor receives
`koan:identity-operator`, and application-scoped roles cannot confer that reserved authority.

The current HTTP slice covers capability descriptors; bounded role list/get/create/edit/disable/
retire; bounded assignment list/assign/revoke; policy read/update/reset; current-subject effective
access; and bounded proposed-change preview. Assignable-role directories and scoped audit history
remain closed until their provider-paged authority and non-forgeable audit ownership are proven.
Viewing definitions, inspecting assignments, assigning roles, editing definitions, editing policy,
and previewing others are separately authorizable. Core remains the only owner of
mutation rules, ceilings, resolution, evidence and commit checks. Web verifies actor, tenant and
ancestry server-side, validates DTOs, requires expected versions (`ETag`/`If-Match` where applicable),
projects safe responses and maps errors. Cookie mutations require antiforgery; bearer clients use the
configured authentication and CORS posture. Generic CRUD, bulk, import and relationship routes must
not create an alternate mutation path. A bundled settings UI remains outside this delivery.

## 6. Delegation and mutation safety

Keep distinct: exercising a capability, assigning a role, editing its definition, changing a scope
policy, transferring ownership and delegating those administrative powers.

The initial implementation should allow an owning authority to define roles, with scoped stewards
assigning an explicitly approved role set. Do not infer delegation from "the actor currently has
this capability" or from cosmetic role rank. Validate tenant, scope, propagation, target eligibility,
expiry and conditional limits against an authoritative delegation ceiling.
Validate the effective grants produced by both role defaults and applicable audience overrides;
assigning a badge-only role selected by a powerful override is still an access grant. Likewise,
policy edits that give an assigned role new authority must pass the policy editor's grant ceiling.
Default capability lists alone are not a complete delegation check.

Editing a populated role changes existing subjects' authority. Validate that effect, including
existing descendant assignments, not only future role assignments. An approved role identity cannot
mean automatic approval of every future enlargement of its definition: use approved versions/grant
envelopes or require explicit reapproval. Pick and document one safe behavior in the first slice.
Definition edits outside the actor's authority must fail without partially changing grants.

Retirement/deletion, assignment, role editing, invitations that widen audiences and ordinary policy
edits are guarded domain operations; exposing Entity CRUD is not sufficient authorization. Generic
write paths must not bypass those operations. Ownership transfer is an explicit app operation.

Policy and binding mutations carry expected versions and attributable before/after evidence. Recheck
authoritative authority at the mutation boundary. Select actual storage concurrency mechanisms and
state their guarantees; an in-process lock alone is not a multi-instance transaction. Do not claim
atomic revocation or audit durability merely because the data provider exposes a native feature.

## 7. One enforcement and query path

Conceptually support Check, Constrain, Explain, Preview and Describe through existing Koan seams.
Public names must follow Koan's normal plain async verb grammar, not invented Async-suffix APIs.

The resolved access plan must carry the decision, the supported per-action row constraint and the
source facts used to explain it. Integrate at the existing enforcement owner; AND-compose with
existing `EntityAccess<T>` constraints and mandatory guards rather than replacing them. Production
checks, previews and per-row `can` must not independently reconstruct policy.

Cover collection, by-id, relationship, count and mutation surfaces that the integration advertises.
Custom actions, background jobs and MCP must use the same authoritative access owner; a new package
reference does not magically guard arbitrary application code or raw data calls. Document each
supported integration boundary and require explicit action intent where it cannot be inferred.
Read current SEC-0004/SEC-0008 implementation before selecting the exact interception point.

### Bounded data and provider truth

- Apply authorization in the database query before pagination, counts or aggregation. Stable
  pagination must not disclose unauthorized rows or totals.
- A boolean callback valid for one record is not automatically a query plan. Require supported
  translation for collection use, or reject that operation with a corrective explanation.
- Never load all rows, walk all memberships or construct an unbounded permitted-ID list as a hidden
  fallback. Bound role/member directories, fact resolution and change previews too.
- Start with a declared parent-scoped listing shape plus by-id access. Cross-scope directories may
  need indexed joins or authorization projections; add only with measured provider support.
- Measure the actual Koan adapter path, including query/pagination pushdown. Do not claim arbitrary
  provider parity. Start with MongoDB for Tangent's consumer path where available; use a second
  existing adapter for the shared supported subset and state unsupported shapes explicitly.
- A projection/materialized access index needs a defined update/revocation protocol. A stale index
  must not become a reason to release newly unauthorized content.

### Freshness and non-request surfaces

Cache keys include tenant, stable subject, relevant credential limits and policy/binding/restriction
versions. Expiry participates in cache validity. Long-lived tokens must not permanently freeze roles.
At minimum, a new operation begun after completed revocation must use revoked authority, and a
mutation begun before revocation must encounter the documented fresh commit-boundary check.
Prove races in the chosen implementation; report any narrower guarantee accurately.

Revocation does not retract previously delivered bytes or automatically cancel every in-flight
operation. Streams/subscriptions must reauthorize before subsequent protected delivery or close;
exports/jobs must apply their documented fresh checks at bounded continuation points. These are
integration responsibilities, not promises obtained from a version number. SSE/UI invalidation is
an optimization and notification mechanism, not the enforcement mechanism.

## 8. Explanations, previews and agents

Return stable machine-readable reasons with optional human text, contributing sources, the winning
override/default provenance, limits and evaluated policy version. Unknown or unauthorized resources
must not leak existence, ancestry, private role names, members or restriction details in explanations.
Expose richer traces only under explicit diagnostic/management authority.

Preview a participant's effective combined roles and applicable conditions, not just a selected
role in isolation. Preview uses the production resource constraints and can simulate proposed edits
without saving them. It is not write impersonation. Proposed changes carry a base version; stale
previews cannot authorize a later save. Scope and paginate impact previews rather than scanning the
entire subject/resource graph for a cosmetic count.

Descriptors can explain a compact agent remit: available scope, valid actions, typed limits and safe
next steps. Tool discovery filters affordances but execution still checks current authority.
Credential changes must preserve subject identity. LLM output, a role label or an instruction in
content is not authority. App-specific human ownership and pause guards apply equally to agent
ownership-derived actions and ordinary role grants.

## 9. Compatibility and exclusions

Preserve existing IdentityRole and tenant Membership behavior through explicit compatibility
contributions where appropriate. Do not silently reinterpret every legacy role claim as an
application-scoped role or confer reserved host authority through tenant bindings. Enrollment into
the new fail-closed action model must be explicit and observable, not a global behavior change to
unrelated Koan applications. Document any intentional contract changes rather than inventing a
permanent dual authorization engine.

The first implementation does not require:

- Role-to-role inheritance, arbitrary multiple-parent graphs or distributed relationship services.
- Administrator-authored scripts, configurable deny/allow precedence or a general policy language.
- A workflow/approval engine, budget reservation system or infrastructure-access broker.
- A complete Discord clone, universal admin console or Tangent UI changes in Koan.
- A full adapter certification campaign or package publication as a prerequisite to the first proof.

Typed parameter support may begin with the conditions actually used by the first fixtures. Do not
advertise a generic conditional/query feature whose real path has not been exercised.

## 10. Dependency-ordered implementation slices

Keep useful increments small. These are bounded work items, not separate epics.

1. **Resolve and persist:** choose the existing owner, register the capability/scope vocabulary,
   persist roles and scoped bindings, implement the v1 evaluation cases and guarded delegation.
   Prove defaults, replacements, correlation, expiry, tenant isolation and populated-role edits.
2. **Enforce and explain:** integrate one immutable plan into real Koan query/by-id/mutation and
   preview paths, retaining existing constraints. Prove actual `AddKoan()` composition and provider
   receipts. Include at least one non-HTTP caller so Web is not an alternative authorization world.
3. **Make it consumable:** provide headless role/assignment/policy operations and safe descriptors,
   a small document-workspace fixture, and an exact Tangent adoption recipe. Coordinate the real
   Speakers-only public Topic journey with Tangent's agent. The consumer owns its UI/schema adoption.

The first tangible consumer journey is: create Speakers, assign two participants, make a Topic
publicly readable but Speakers-only for replies, preview visitor/member/speaker, and revoke a
speaker while clients remain connected. Framework tests may use synthetic discussion entities;
do not import private Tangent data, credentials, participant identifiers or machine-specific logs.

Completion requires a reusable working slice, not just Entity schemas, inert catalogs or an
isolated resolver test. Integrate the same resolver before expanding to additional features.

## 11. Acceptance contract

Use focused owner and consumer tests with a real host. Independently red-team the authorization
boundaries before declaring the slice ready. Record exact commands, source revisions, provider
identity and limitations; reserve the full release ratchet for a real certification boundary.

| Case | Required result |
|---|---|
| Member plus Reader | Reader does not negate an otherwise valid Member reply grant |
| Public selected-contributors scope | Anonymous reads; ordinary member cannot reply; a selected role can reply |
| Same-scope replacement | Excluded default grants cannot bypass the replacement |
| Inherited replacement plus local binding | Local binding alone cannot bypass it; an authorized child replacement can make a visible exception |
| Single-child guest | Explicit child grant works without parent membership; parent and sibling metadata remain inaccessible |
| Ancestor mandatory ceiling | Child policy or role cannot override an applicable ban, pause or publication ceiling |
| Cosmetic role and rename/reorder | Appearance changes do not alter authority; explicit local role selection can grant an action |
| Role/binding lifecycle inside overrides | Disabled/retired roles and revoked/expired bindings stop matching replacement audiences as well as default grants |
| Correlated conditional grants | The department/amount example never manufactures the broader cross-product grant |
| Capability and scope validation | Unknown actions, roles, invalid versions, forged ancestry and cross-tenant bindings fail closed |
| Delegated assignment | Actor cannot assign outside approved role/scope/propagation/expiry limits or exceed its ceiling through self-assignment; expressly authorized self-assignment is valid |
| Audience-based grant escalation | A badge-role assignment or audience edit cannot evade delegation ceilings by leaving default role capabilities empty |
| Populated-role expansion | Editing a role cannot silently exceed existing delegation approvals or partially apply |
| Reserved authority | Tenant/application roles cannot confer host authority; app ownership guards remain effective |
| API bypass | Generic CRUD, bulk/relationship paths and alternate callers do not bypass role/policy mutation guards |
| Query/check/preview parity | Collection, by-id, relationships, counts and applicable per-row actions agree under the same facts |
| Query capability boundary | Supported adapter pushes filter and pagination down; unsupported shape rejects without scanning everything |
| Privacy-safe preview | No write impersonation or private source/role/member leakage through reasons or impact counts |
| Revocation, expiry and races | Fresh operations deny revoked grants; stale mutation/preview is rejected under the documented concurrency boundary |
| Restart and credential rotation | Definitions/bindings survive restart; replacing a credential does not replace the subject |
| Guard and dependency behavior | Missing required action prerequisites/guards deny; defined recovery remains narrowly available |
| Non-Tangent reuse | Document workspace works with different actions/scopes and no Tangent constants in the engine |

### Web acceptance matrix

| ID | Required HTTP evidence |
|---|---|
| W01 | Core-only `AddKoan()` has no management routes; the Web complement discovers them; missing auth/scope dependencies and anonymous management fail closed. |
| W02 | A delegated steward manages only approved scoped assignments, edits definitions only under separate authority, and cannot use global identity-operator endpoints. |
| W03 | Route/body/loaded-target scope mismatch, foreign binding revocation and cross-tenant assignment reject before mutation without disclosure. |
| W04 | Overposted actor, issuer, tenant, parent, audit, version and protected fields cannot confer authority, relocate a target or disguise attribution. |
| W05 | The Speakers public-read/selected-reply journey and an unrelated document-workspace journey agree across HTTP preview and actual enforcement. |
| W06 | The assignable-role picker applies actor, target, scope, propagation, expiry and condition ceilings; forged selections and excessive self-assignment still deny. |
| W07 | Populated-role enlargement, badge-role audience selection and audience edits validate their effective indirect grants and leave no partial expansion on denial. |
| W08 | Generic entity, patch, bulk/import, relationship and legacy routes are absent or identically guarded and cannot confer reserved authority. |
| W09 | Unknown actions/roles, retired selectors, malformed/unsupported policies and forged ancestry fail closed; empty replacement and child replacement semantics remain exact. |
| W10 | Revoked/expired bindings and disabled/retired roles stop both defaults and override selectors after restart; protected roles reject unauthorized edits. |
| W11 | Large role/assignment/scope directories prove authorization-filter and pagination pushdown, authorized counts and bounded work on MongoDB and the declared second-adapter subset. |
| W12 | Preview is authorized, privacy-safe, non-mutating and non-reusable; hidden facts and impact counts do not leak. |
| W13 | Concurrent writes prove real provider CAS: one current write succeeds, stale `If-Match` is `412`, and stale preview cannot authorize save. |
| W14 | Cookie mutations require valid antiforgery; bearer and browser-origin behavior use configured authentication/CORS while server authorization remains authoritative. |
| W15 | Preview/impersonation never grants management authority and every mutation preserves verified real-actor attribution. |
| W16 | Assignment/revoke retries converge without duplicate grants or accidental expiry renewal; replay after lost authority cannot mutate or reveal cached protected results. |
| W17 | HTTP and a supported non-HTTP caller have decision parity and the documented post-revocation/commit-boundary freshness behavior. |
| W18 | Errors expose stable safe reasons; successful audit rows identify actor, scope, target, version and change; failures/retries do not create false success evidence. |

On changed shared contracts, sweep reflection and string-based consumers as well as compile-time
callers. Respect Koan's shared build-output limitation: do not run parallel builds/tests against
the same output tree. Provider parity and concurrency claims require corresponding evidence.

## 12. Progress and handoff

This section is the sole live ledger for this specification. Keep entries short and link durable
test receipts/current source rather than copying status into additional plans.

### Selected implementation contract

Contributor exploration selected the existing functional `Koan.Identity` package as the owner; the
engine will not add a second package or module. Persisted records are explicit-tenant
`ScopedRoleDefinition`, `ScopedRoleBinding`, `ScopedRolePolicy` and `ScopedRoleScope` entities.
Application authority enters through a discovered `IScopedRoleAuthorityContributor`; the framework
has no implicit administrator or owner. Public operations bind the real actor through
`IIdentityActorAccessor`, while effective-subject lookup is a separate seam so impersonation cannot
rewrite audit attribution. The public headless owner is one scoped `RoleEngine` with
plain async verbs (`Define`, `Assign`, `Replace`, `Revoke`, `Plan`, `Check`, `Preview`, `Describe`).

All engine-owned entity writes are lifecycle-guarded and can run only inside the engine's internal
mutation scope, so generic Entity CRUD cannot bypass delegation validation. New identities use the
adapter's atomic insert-only receipt; mutable records use expected-version conditional replacement.
Bindings retain the role's approved authority version. Cosmetic definition edits do not change that
version; grant/status edits do, and stale bindings stop contributing until expressly reapproved.
An immutable plan preserves complete alternative clauses and is the unit later consumed by Web,
non-HTTP callers, preview and explanation. Resolution bounds ancestry and subject bindings through
`RoleEngineOptions`; unknown or unsupported scope/capability/condition versions reject rather than
falling back. MongoDB is the first provider target; the shared filter/CAS subset will be proven on a
second existing adapter before provider parity is claimed.

| Date | State | Evidence / next action |
|---|---|---|
| 2026-09-13 | Specification authored; implementation authorized by Leo; no implementation claimed | Baseline `30586ebf8c878fec04047aceefdad0e261c8c532`; next: Koan owner performs contributor exploration and starts slice 1 |
| 2026-09-13 | Contributor exploration complete; slice 1 contract selected | Reused `Data.Insert`, `Data.ReplaceIf`, lifecycle guards, typed options and Identity audit; next: red tests for persistence, v1 evaluation, delegation and role-version reapproval |
| 2026-09-13 | Optional HTTP boundary approved | Existing `Koan.Identity.Web` selected; W01-W18 added; core-first implementation continues before route exposure |
| 2026-09-13 | Slice 1 core prototype green; independent red-team completed | `dotnet test tests/Suites/Integration/Identity/Koan.Identity.Tests/Koan.Identity.Tests.csproj --no-restore`: 110/110 on InMemory. Red-team corrections landed for verified actors, finite expiry, canonical binding identity, policy-version reapproval, exact lifecycle permits, instruction/fast-remove bypass, mandatory guards and audit evidence. Not complete: provider-backed commit-boundary authority proof, Mongo/second-adapter receipts, query/Web integration and authorized directories remain open. |
| 2026-09-13 | Slice 2 headless query boundary and first real-provider receipt green | Mutation authority is re-read through the selected contributor and captured scope versions at the one-shot lifecycle boundary. `Constrain`, `Query`, `QueryWithCount`, `Get` and `Count` share one immutable plan plus provider-pushed tenant-and-scope filters; paginated access rejects adapters without provider-bounded paging. Identity is 112/112 and the isolated SQLite receipt is 1/1, proving pushed filtering, paging/count, insert-only and conditional-replace CAS. Not complete: Mongo parity, optional Web management routes/directories and W01-W18 remain open. |
| 2026-09-13 | Mongo parity and initial optional Web boundary green on `dev` | Isolated Mongo receipt is 1/1 for pushed tenant/scope filtering and paging/count, same-scope-id cross-tenant hiding, post-revocation denial and provider-re-read authority invalidation before dispatch. TestServer + SQLite Web receipt is 1/1 for reference activation, anonymous failure, server-generated role IDs, indistinguishable absent/unauthorized responses, ETag/`If-Match`, real-cookie antiforgery despite a forged authorization header, and global-operator separation. Identity is 115/115; SQLite remains 1/1; both packages build warning-free. Exact correlated direct and policy-derived grant/audience clauses are now optional delegation-envelope ceilings. Assignable-role and scoped-audit routes were withdrawn after independent red-team because their safe provider-paged/read-owner designs are not yet proven. Directory paging remains closed on adapters without provider-bounded paging. The freshness guarantee is a lifecycle-boundary pre-dispatch recheck, not a serializable authority+target transaction. W01-W18 are only partially covered; generic trusted-code reads, consumer/document-workspace, directory-scale, preview-race, concurrent ETag and restart evidence remain open. |

**Current handoff:** the existing Koan task **Report framework status** owns implementation.
The coordinating task **Polish Tangent Space UX** owns consumer adoption. Before production edits,
follow the repository's explore instructions, reconcile exact module/API choices, and proceed with
the approved bounded work; this specification is authorization to implement, not a request for
another general design-approval round. Escalate material semantic changes or missing external
authority. Preserve unrelated work and serialize writes/builds in the shared checkout.

Send the coordinator the chosen contract, supported provider/query shapes, verification evidence,
identified failures/limitations and the next consumer step. Keep framework implementation in Koan.
Do not enroll a moderator, change a live consumer database, rotate credentials or deploy Tangent as
part of this dispatch. Package publication follows existing release authority and playbook; this
spec does not create blanket release authority or permit hand-edited versions.

**Closeout:** when the three slices and applicable acceptance cases are proven, link shipped
guidance/source and mark the ledger complete. Archive the initiative according to normal Koan
practice. If scope is deliberately reduced, name the unsupported behavior; do not label the whole
engine complete on the strength of a resolver-only or schema-only implementation.

## 13. References and rationale

- [SEC-0004](../decisions/SEC-0004-capability-authorization-gate-constrain-project.md): existing
  gate/constrain/project direction; verify current source where later work supersedes it.
- [SEC-0007](../decisions/SEC-0007-koan-identity-module.md): Identity context; the retired role
  catalog is not an instruction to revive an inert path.
- [SEC-0008](../decisions/SEC-0008-access-enforcement-at-the-data-layer.md): data enforcement context.
- [Discord permissions](https://docs.discord.com/developers/topics/permissions): named roles and
  channel overwrites demonstrate the interaction; v1 deliberately specifies simpler override and
  delegation semantics rather than claiming Discord wire or algorithm compatibility.
- [OpenFGA modeling roles](https://openfga.dev/docs/best-practices/modeling-roles): distinguishes
  fixed roles, user-defined roles and resource-specific assignments. No runtime adoption selected.
- [Cedar authorization](https://docs.cedarpolicy.com/auth/authorization.html): precedent for explicit
  permits and overriding restrictions. No policy-language adoption selected.

This is owner-directed product/framework work, not a confirmed new Koan defect report. New defects
discovered during implementation should be recorded with revision, reproducer and consumer impact.
