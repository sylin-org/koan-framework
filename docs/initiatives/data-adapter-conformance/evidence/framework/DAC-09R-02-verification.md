---
type: EVIDENCE
domain: data
title: "DAC-09R-02 Source freeze and bounded ownership verification"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: passed
  scope: DAC-09R-02 focused verification
---

# DAC-09R-02 verification

## Result

PASS. One immutable source catalog is admitted during host composition and frozen before runtime resolution. Duplicate,
replacement, late, and over-capacity declarations reject without changing the prior decision. Source plans, root
repositories, and polymorphic repository views use finite host-owned caches.

Repository and source-integration creation are single-flight under concurrent first use. A failed provider creation
does not poison the cache: the next request can retry, and no over-capacity request constructs an unadmitted provider
resource. Source integrations activate lazily, remain bounded by the frozen source catalog, and are disposed exactly
once. Synchronous disposal refuses an activated async-only integration before losing ownership, so subsequent proper
asynchronous host disposal remains safe.

Literal Direct connection values are deliberately excluded from the host plan cache; each receives an immutable
redacted plan without turning unbounded caller input into retained host state.

## Reproduced checks

| Check | Result |
|---|---|
| Source policy, hosting ownership, and Source Integration matrix | 71/71 PASS |
| Broader Data Core source/hosting regression | 90/90 PASS |
| Concurrent root repository first use | 24 callers, one provider creation |
| Concurrent source-integration activation | 24 callers, one provider creation |
| Failed root/source provider creation | retry succeeds; no poisoned cache |
| Repository/source/plan capacity | rejects before unadmitted provider construction or mutation |
| Async-only source disposal | sync correction then one successful async disposal |
| Initiative protocol | 41 cards, 41 progress rows, 105 primer IDs, 22 packets |
| Mutation protocol | 16/16 PASS |
| Forbidden replacement/sync-over-async search | no matches in the three runtime owners |
| `git diff --check` | PASS; line-ending notices only |

Restore-free builds emitted NU1900 warnings because the sandbox could not reach NuGet vulnerability metadata. All
affected projects compiled with zero compiler errors; the later full clean certification gate still owns environmental
warning disposition.
