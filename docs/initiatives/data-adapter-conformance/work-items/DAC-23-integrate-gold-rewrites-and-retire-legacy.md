---
type: SPEC
domain: data
title: "DAC-23 Integrate Gold Rewrites and Prove Atomic Legacy Retirement"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: reviewed
  scope: SQLite/MongoDB replacement integration, legacy absence, and common checkpoint
---

# DAC-23 — Integrate gold rewrites and prove atomic legacy retirement

| Field | Value |
|---|---|
| Phase / kind | gold / replacement integration gate |
| Depends on | DAC-13, DAC-24 |
| Unlocks | DAC-12 and DAC-22 |
| Primer scope | complete SQLite/MongoDB contracts, replacement manifests, and retirement inventories |
| Production writes | deterministic composition only; no new behavior |
| Owner | Gold replacement integration and retirement verification |

## Meaningful outcome

One reproducible tree contains only the two new gold adapters, with every former implementation and alternate path
absent before either adapter can be certified.

## Required work

1. Reproduce DAC-15's common base and both DAC-13/DAC-24 reviewed atomic changes. Undeclared overlap, conflict, or
   provider-code edit in this card is STOP.
2. Resolve every retirement item to absence across files, compile items, assemblies/types, DI/reflection discovery,
   factories, options/aliases, generated outputs, docs/examples, fixtures, and tests.
3. For each adapter, prove one package/project, activation/election route, registration/factory, repository/native
   execution path per operation, claim source, and adapter-test authority.
4. Reproduce architecture reviews: every moving part has a contract/shared-mechanics/hot-path reason; no copied
   structure, duplicate ownership, warm-path discovery, `Legacy`/`V2`/`Compat`, bridge, feature-flag fallback, shadow
   registration, or unreachable leftover remains.
5. Build clean outputs and run shared, SQLite, and MongoDB smoke/boundary suites. A behavioral failure returns to the
   appropriate ground-up card as a black-box case, then its review and this integration rerun.
6. Seal one source/dependency fingerprint and complete retirement/architecture verdict. DAC-12 and DAC-22 must cite and
   independently reproduce this exact identity.

## Definition of done

- [ ] The tree is exactly the common base plus both complete replacements and no unrelated change.
- [ ] Every retirement entry resolves to absence or an explicitly ratified public identity newly implemented.
- [ ] Each gold has one selected implementation and no compatibility/shadow/dead path.
- [ ] Both architecture reviews and focused boundary suites pass.
- [ ] DAC-12 and DAC-22 are bound to one reproducible checkpoint.

## Stop conditions

Different bases, incomplete retirement, overlap/conflict, dual registration, unexplained moving part, copied/dead path,
failed boundary test, or any provider behavior edit here blocks certification.
