---
type: EVIDENCE
domain: data
title: "DAC-09R-03 Operation effect chokepoint verification"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: passed
  scope: DAC-09R-03 focused verification
---

# DAC-09R-03 verification

## Result

PASS. `DataOperationEffect` is now the one effect vocabulary used by source plans, instructions, registered operation
plans, diagnostics, Direct sessions, and Direct transactions. The duplicate public `OperationEffect` type is removed.

Raw SQL instruction identities default to `Unknown`; `Scalar`, `Query`, `TResult`, command text, comments, common-table
expressions, and multi-statement text never grant Read authority. The two `StartsWith("select")` extension paths and the
result-inferred `Data<TEntity>.Execute<TResult>(string)` overloads are deleted. Callers who use the expert Direct escape
hatch can state the decision once with `.Effect(Read|Write|SchemaOrAdmin)`; a transaction inherits that exact effect.

Direct, transaction, Entity instruction, and registered-read paths gate the frozen source ceiling before physical route,
connection, callback, or provider construction. Registered operations bind parameters and reject missing/invalid lanes
before source integration activation. Opaque registered bindings still require a permanently selected, provider-enforced
read lane. Caller cancellation now reaches reflection-backed Entity instructions and asynchronous transaction
commit/rollback, and failed Direct opens dispose their connection.

## Reproduced checks

| Check | Result |
|---|---|
| Focused source policy, Source Integration, and Direct matrix | 75/75 PASS |
| Broader Data Core Direct/source regression | 84/84 PASS |
| Relational Family regression | 16/16 PASS |
| Segmented Direct first-boundary case | 1/1 PASS |
| Normal-host unknown instruction | rejects with zero adapter factory calls |
| Opaque/no-lane and invalid-parameter registered reads | reject with zero integration activations |
| Adversarial scalar/query/CTE/multi-statement text | remains `Unknown`; rejects before route/provider work |
| Framework/Family negative search | no duplicate effect enum, SQL-prefix inference, result-inferred raw overload, or dropped Direct token |
| Solution build | PASS, 0 compiler errors |
| Dynamic surface map | 52 surfaces, 3,161 public entries, 422 critic matches |
| Framework scorecard projection | 105/105 rows |
| Initiative protocol | 41 cards, 41 progress rows, 105 primer IDs, 22 packets |
| Mutation protocol | 16/16 PASS |
| `git diff --check` | PASS; line-ending notices only |

Three legacy relational connector repositories still contain local SQL-text result-dispatch helpers. R03 does not edit
connector implementations: those paths are neither Framework policy authorities nor gold references, and remain
explicitly assigned to empty-root gold/fleet adapter cards. The Framework and Relational Family owners contain no such
inference.

Restore-free builds emitted five NU1900 warnings because the sandbox could not reach NuGet vulnerability metadata. The
complete solution compiled with zero errors.
