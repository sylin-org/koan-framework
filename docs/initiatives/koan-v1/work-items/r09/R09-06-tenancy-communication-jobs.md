---
type: SPEC
domain: framework
title: "R09-06 - Compile Hard Context Across Communication and Jobs"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-16
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-16
  status: passed
  scope: Core hard-context bridge, Communication trust/context realization, Jobs durable realization, and focused local/RabbitMQ evidence
---

# R09-06 — Compile hard context across Communication and Jobs

- Tranche: `T7A — semantic composition prerequisite`
- Status: `passed`
- Depends on: R09-05 Core hard segmentation and pillar-owned realization receipts
- Unlocks: honest cross-process guarantees and the remaining R09 evidence/change surfaces
- Owner: Core segmentation-to-carriage bridge; Communication ingress trust; Jobs durable execution context

## Explore gate

**Task:** Join Core's hard segmentation dimensions to Core's opaque context carriers once, then make
Communication and Jobs consume that compiled contract at their existing terminal/ingress chokepoints.

**Application intent:** “If tenant A sends, raises, or submits work, application handlers and work-item
I/O run as tenant A. Adding an eligible provider changes reach or durability without changing business
code. Koan refuses any route that cannot preserve the required context.”

**Public expression:**

```csharp
builder.Services.AddKoan();

await order.Transport.Send(ct);
await order.Events.Raise<OrderApproved>(ct);
await order.Job.Submit(ct);
```

Reference `Koan.Tenancy` to require tenant isolation and reference the desired Communication/Jobs
packages. No tenant envelope, queue key, carrier, signature, ledger partition, or provider API appears
in application code. `Tenant.Use(id)` remains the explicit tool for trusted non-request flows;
`[HostScoped]` remains the explicit declaration for control-plane Entity types.

**Guarantee/correction:** For every hard dimension applicable to the Entity type, the terminal must bind
an ambient value and capture a carrier that declares coverage for that dimension. Ingress must prove the
carrier's minimum trust, require every applicable covered axis, restore it before application code or
work-item I/O, and re-bind the dimension under the restored scope. Missing coverage, missing context,
insufficient trust, malformed payload, or unsupported version rejects before the handler. Facts never
claim route, topology, confidentiality, or exactly-once guarantees merely because logical context is
restored.

### Documentation read

- `docs/architecture/principles.md`: Entity-first, Reference = Intent, fail-loud, progressive
  complexity, and one canonical path constrain the public result.
- `ARCH-0113`: owns Entity Events/Transport meaning, local-first equivalence, adapter election,
  context-aware delivery identity, and the separation of logical context, routing, topology, and privacy.
- `ARCH-0100`: owns capture-before-await, durable bag persistence, host-trusted restore, null-bag
  suppression, coalesce identity, and fail-before-handler behavior for Jobs.
- `R07-18`: business channels are optional reach policy, not tenant or infrastructure vocabulary.
- Communication and Jobs `TECHNICAL.md`: confirm the current router/ledger ownership and supported
  assurance boundaries.

### Code read and existing chokepoints

- `TransportCoordinator` and `EventCoordinator` already capture once before source enumeration, encode
  one opaque context bag per operation, and publish through one elected route. They are the correct
  outbound enforcement points.
- `CommunicationRouter.Dispatch` validates the wire and resolves one typed binding;
  `CommunicationIngress` creates the DI/AppHost scope and restores context before dispatch. They are the
  correct inbound enforcement points.
- `InProcessCommunicationRuntime` is the faithful zero-configuration floor and dispatches inside the
  host boundary. RabbitMQ HMAC-signs the complete wire body and verifies it before declaring
  authenticated ingress. Its shared exchange/queues provide logical restoration, not per-tenant
  physical topology or confidentiality.
- `JobCoordinator` captures once before the first await, folds the bag into coalescing, saves the
  work-item, and appends the host-scoped ledger row. `JobOrchestrator.ExecuteClaimedAsync` restores before
  load and retains the scope through handler, settle, retry, and chain advancement. These are the correct
  durable chokepoints.
- `JobWakeCoordinator` carries only a lossy context-free latency hint. The ledger remains the truth and
  must not be coupled to tenant routing.
- `SegmentationPlan` and `KoanContextCarrierRegistry` independently compile the two halves of the same
  hard-context promise. The missing piece is one Core-owned join, not new Tenancy-to-pillar adapters.

### Constants, options, and public DTO audit

- Existing configuration remains sufficient: Communication provider/channel/capacity/payload options,
  RabbitMQ endpoint/trust/prefetch/timeout options, Jobs execution/ledger options, and Tenancy posture.
- New stable operation, fact, reason, capability, realization, and coverage IDs belong in each owning
  project's existing `Infrastructure.Constants`.
- No new application option, request, response, controller, Entity facet, or DTO is required.
- Adapter ingress trust is provider ABI metadata, not application configuration. The built-in floor is
  host-trusted; RabbitMQ is authenticated only after its body signature verifies; undeclared providers
  default to unverified.

## Coalescence decision

Create one Core `SegmentationContextPlan` that memoizes the applicable dimension-to-carrier obligations
per CLR subject. A carrier may declare which hard dimensions it faithfully represents. The plan owns
capture validation, restore validation, and the strongest composed ingress requirement. Communication
and Jobs call this plan; neither reads Tenancy nor implements axis-specific logic.

Move ingress-trust choice out of per-delivery adapter code. Each adapter declares one immutable trust
posture in its descriptor; its host dispatch closure applies that posture. Communication election rejects
a Transport/Events provider whose declared trust is weaker than the composed carriers require. This
leaves adapters responsible only for the mechanism that substantiates their declaration.

This specificity is deliberate:

- Wider is wrong: Core must not render tenant queue names, RabbitMQ topology, or job-ledger placement.
- Narrower is wrong: separate Communication and Jobs validation would duplicate the same dimension/
  carrier join and drift as future hard context axes arrive.
- Tenancy remains pure: it declares one dimension and one carrier relationship; future capabilities use
  the same contract without editing Communication or Jobs.

## Guarantee levels

| Level | Local Communication | RabbitMQ Communication | Jobs |
|---|---|---|---|
| Logical application context | enforced or rejected | authenticated, enforced or rejected | host-trusted, enforced or dead-lettered |
| Route/binding segmentation | typed contract/group only | typed contract/group only | work type/lane/coalesce plus context fingerprint |
| Physical tenant topology | shared process queues | shared exchange/consumer queues | shared host/control-plane ledger |
| Work-item state isolation | handler's Data plan | handler's Data plan | Data plan across load/execute/settle |
| Confidentiality | process boundary only | not provided by this connector | provider/deployment owned |
| Delivery | process-memory settlement | durably acknowledged publication; remote settlement unavailable | at-least-once execution; wake is best effort |

These distinctions must be visible in composition facts. “Tenant A is only received by tenant A” means
the application receiver is invoked under authenticated tenant A and all tenant-scoped state operations
bind A. It does not imply a tenant-dedicated RabbitMQ queue, encrypted payload, or exactly-once effects.

## Red proof and implementation sequence

1. **Core bridge:** prove typed memoization, missing carrier declaration, absent captured axis, restored
   re-bind, insufficient trust, host-scoped bypass, value privacy, and the empty allocation-light floor.
2. **Local Communication vertical:** bind/capture at Send/Raise and validate/restore at ingress. Convert
   the local provider to immutable host-trust declaration. Prove tenant A/B isolation, missing-context
   refusal before acceptance/handler, set/stream sealing, and no ambient leakage.
3. **Provider qualification:** make trust part of adapter election/host dispatch. Prove an unverified
   provider is ineligible when authenticated axes are composed; prove RabbitMQ's verified HMAC path and
   report logical versus topology/privacy assurances honestly.
4. **Jobs vertical:** replace direct registry calls with the same Core plan at submit/source/trigger and
   execute. Preserve bag-based coalescing, null suppression for host-scoped jobs, retries/chains, shared
   ledger ownership, and context-free wake semantics. Prove refusal before work-item load/handler.
5. **Evidence/deletion closure:** register Communication and Jobs realization receipts only for executed
   coverage, reconcile current ADRs/technical docs, sweep for alternate trust/context authorities, and
   run only named Core, Communication, RabbitMQ, and Jobs/Tenancy cells.

## Ergonomics assessment

- Human: the code continues to say only Send, Raise, and Submit; package intent supplies the guarantee.
- IntelliSense: no routing or context infrastructure is added to Entity's application-facing ring.
- Agent: one stable guarantee matrix and safe correction replaces inference across envelopes, adapters,
  and ledger code.
- Operator/reviewer: startup/HTTP/MCP distinguish authenticated logical isolation from shared physical
  topology and unsupported confidentiality/settlement claims.
- Machine: subject obligations and provider trust are compiled once; hot paths bind/capture once per
  semantic operation and do no contributor discovery or provider negotiation.

## Focused verification matrix

| Cell | Required result |
|---|---|
| Core join | each applicable hard dimension has exactly one declared carrier; missing/duplicate coverage rejects safely |
| Capture | missing ambient or absent carrier payload rejects before publication/ledger append |
| Restore | absent/unknown/malformed/under-trusted context rejects before application code and ambient state unwinds |
| Local | Send/Raise in A execute only under A; A/B concurrent flows do not contaminate; host-scoped types remain valid |
| Election | unverified providers cannot carry authenticated context; direct intent never falls back |
| RabbitMQ | signed body restores tenant; tampered/unsigned body is rejected; facts do not claim tenant queues or encryption |
| Jobs | submit/source/trigger, coalesce, load, handler, settle, retry, and chain preserve one captured context |
| Jobs control plane | ledger remains host-scoped/shared; wake remains context-free and correctness-independent |
| Economy/privacy | no runtime contributor discovery, per-item plan compile, context values in facts, or secrets in errors |

## Stop conditions

- Stop if the Core bridge needs to know tenant, RabbitMQ, Jobs, or a physical encoding.
- Stop if an application must configure context carriage for the common path.
- Stop if provider reach silently falls back when direct intent is ineligible or unavailable.
- Stop if facts collapse logical restoration into physical isolation, confidentiality, or exactly-once.
- Stop if the implementation adds a second context envelope, tenant-specific Jobs ledger, or alternate
  public Send/Raise/Submit path.

## Implementation closure

- Core now compiles one memoized `SegmentationContextPlan` per CLR subject. It joins hard dimensions
  to module-owned opaque carriers, rejects missing or duplicate coverage, captures required axes, and
  restores then re-binds before application work. Core knows no tenant token, broker, or job encoding.
- `IKoanContextCarrier` can declare the hard dimensions it faithfully represents. Ordinary capture
  remains tri-state. A carrier may materialize a deterministic resolved value only when a hard typed
  operation requires it; Tenancy uses that seam for the Development fallback without changing ambient
  state or making host-scoped work tenant-scoped.
- Communication owns one `CommunicationContextPlan` over Core. Send/Raise bind and capture once;
  ingress requires the applicable axes, restores them using immutable adapter provenance, and re-binds
  before filters or handlers. The in-process floor is host-trusted; RabbitMQ is authenticated after
  HMAC verification; absent, weak, or unknown trust declarations are refused before provider start.
- Jobs owns one `JobsContextPlan` over Core. Submit/source/trigger capture before persistence; execution
  restores before work-item load and retains the scope through handler, settle, retry, and chain work.
  The shared host-scoped ledger remains the control plane, Data owns work-item isolation, and the wake
  signal remains a context-free best-effort latency hint. The unused public compatibility constructor
  that manufactured an empty plan is deleted; DI composition is the single runtime construction path.
- Communication and Jobs register pillar-owned `ISegmentationRealization` receipts. Their composition
  facts state logical restoration separately from typed routing, shared topology/ledger, confidentiality,
  settlement, and at-least-once bounds. No context value enters facts or correction text.
- The supported manifest path exposed a canonical-identity mismatch: Tenancy declared `Koan.Tenancy`
  while source/package intent records `Sylin.Koan.Tenancy`. The module ID now uses the canonical
  assembly identity, so Reference = Intent activates the carrier deterministically instead of relying
  on degraded assembly discovery.

No application grammar was added. `AddKoan()` plus package intent still supplies the local floor, and
the business code remains `Send`, `Raise`, and `Submit`.

## Focused execution record

| Cell | Result |
|---|---|
| Core `SegmentationContextPlanSpec` | 8/8 passed |
| Tenancy `TenantContextCarrierSpec` | 11/11 passed |
| Communication provider election | 8/8 passed |
| Communication Transport/Event context plus composition facts | 10/10 passed |
| Jobs/Tenancy `DurableCarrierSpec` | 14/14 passed |
| RabbitMQ complete connector slice | 9/9 passed; the final authenticated-context/facts rerun passed 2/2 |

The focused cells prove fallback materialization, refusal before publication/persistence, local A/B
context isolation, trust-qualified provider election, authenticated network restoration, retry/chain/
coalesce carriage, control-plane separation, and value-free evidence. No release-certification suite,
publication, push, tag, or remote mutation ran.
