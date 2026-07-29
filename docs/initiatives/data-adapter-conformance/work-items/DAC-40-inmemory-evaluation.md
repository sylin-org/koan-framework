---
type: SPEC
domain: data
title: "DAC-40 Evaluate and Certify the InMemory Entity Adapter"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: reviewed
  scope: InMemory adapter evaluation prompt
---

# DAC-40 — Evaluate and certify the InMemory Entity adapter

| Field | Value |
|---|---|
| Phase / kind | fleet / audit-certification |
| Depends on | DAC-30 |
| Primer scope | dynamically selected InMemory manifest |
| Production writes | forbidden |
| Owner | Adapter(InMemory); Core KeyValue rows split |

## Meaningful outcome

Koan has a fast dockerless semantic sentinel whose claims are honest about process-local storage, lifecycle,
atomicity, optimization, and durability.

## Approved vertical-slice exploration

**Task:** Evaluate the current InMemory Entity adapter against the primer and correct only concrete adapter-owned gaps.

**Application intent:** Use ordinary Entity persistence in a fast, explicitly ephemeral Koan host without files,
containers, network services, or provider-specific setup.

**Public expression:** Reference `Sylin.Koan.Data.Connector.InMemory`, call `AddKoan()`, define `Entity<T>`, and use its
normal save/get/query/remove verbs. Configure `Koan:Data:Sources:{name}:Adapter=inmemory` only when placement must be
pinned. There are no runtime prerequisites beyond the current process.

**Guarantee/correction:** Awaited operations are visible within the same host/source/partition and never survive host
disposal. Unsupported durability, cross-process coordination, provider-bounded streaming, and unproved atomicity are
not inferred; a required unsupported guarantee rejects before partial mutation.

**Complete intent surface:** The package reference, ordinary Koan bootstrap and Entity model are sufficient. Source
selection, access/lifecycle policy, and partition/source context are optional only when the application makes those
decisions explicitly.

**Public concepts:** `inmemory` is the only provider-specific concept and expresses deliberate ephemeral placement.
Source, partition, access, lifecycle, query, batch, and Entity verbs retain their provider-neutral meanings.

**Docs read:** The adapter primer requires truthful receipts, fail-closed declines, bounded work, host ownership, and
no provider-bounded streaming claim for InMemory. The responsibility map assigns policy/orchestration to Data and
scan/filter/batch mechanics to the KeyValue family. Architecture principles keep Entity as the application surface and
runtime paths host-owned and thin. This card fixes process-local lifecycle and atomicity as the evaluation focus.

**Code read:** `InMemoryAdapterFactory` only binds one host store and source; `InMemoryDataStore` owns concurrent
dictionaries keyed by source/type/partition; `InMemoryRepository` supplies six KeyValue primitives and capability
claims; `KeyValueStore` owns query, guards, batch, and instructions. Current tests cover CRUD, sorting, partitions,
routing, managed scope, batching, instructions, and capability publication.

**Reusing:** Data source election/policy; Entity facade; the KeyValue family contract; host DI lifetime; shared filter,
sort, isolation and conformance oracles. No adapter options or public DTO is required.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| behavior cases only when a claimed guarantee lacks evidence | `tests/Suites/Data/Connector.InMemory/**` | prove host isolation, snapshot semantics, cancellation, and atomicity without implementation coupling |
| bounded adapter correction only after a RED case | `src/Connectors/Data/InMemory/**` | keep provider-local mechanics local; no speculative framework seam |

**Coalescence:** The closest pattern is the already-selected `KeyValueStore<TEntity,TKey>` family seam. It owns shared
query, guard, batch, and instruction meaning; the adapter owns only process-memory storage. Keep the factory/store/repo
split if behavior is green, remove false claims rather than add machinery, and change the family only if a concrete
cross-adapter contract cannot be expressed by its primitive seam. No superseded InMemory path is currently visible.

**Ergonomics:** The common path is package + `AddKoan()` + Entity. IntelliSense exposes no InMemory service or
repository. Explicit placement remains one source setting; declined guarantees fail through existing Data errors.

**Constraints satisfied:** Entity-first access; no HTTP surface; stable provider identifiers already live in
`Infrastructure/Constants`; no options are needed; no placeholder or alternate runtime path exists; InMemory streaming
remains rejected; README/TECHNICAL change only with observed behavior.

**Risks:** The current `AtomicBatch` claim appears stronger than the family batch implementation, which performs
separate bulk-upsert and delete phases. Stored records also retain Entity object references, so unsaved caller mutation
may alter stored state. Both require black-box proof before any production change.

## Execute

1. Pin identity and create the complete `evidence/inmemory/` packet.
2. Inventory every Entity, query, batch, Direct/instruction, source-policy, lifecycle, isolation, and alternate path.
3. Run all applicable shared cells. The adapter itself is the provider; do not substitute a mock repository.
4. Prove host isolation, concurrent access, cancellation, bounded caches/soak, and exact behavior after host restart.
5. Decline durability, native plans/indexes, or provider atomicity unless the implementation genuinely proves them.
6. Audit any Core KeyValue scan/sort/page receipt: process-memory execution may be valid provider work, but stronger
   native/index/provider-bounded claims still require truthful receipts.
7. If RED, write bounded one-owner remediation cards from TEMPLATE and BLOCK. A fresh session reruns DAC-40 after they
   pass. Do not repair production in this card.

## Verification

- Complete InMemory suite, strict Forge, shared Data/Core cells, restart/host-isolation/soak, and packet validator.
- Mutation removes a declared capability behavior and must make its cell red.

## Definition of done

- [ ] Every Observed row PASS and every Declined path has negative proof.
- [ ] The adapter is admitted to the dockerless per-PR sentinel lane.
- [ ] Runtime/public claims match the packet without implying persistence it does not provide.

## Stop conditions

Any Framework/KeyValue RED or production edit stops certification and creates a separate remediation card.
