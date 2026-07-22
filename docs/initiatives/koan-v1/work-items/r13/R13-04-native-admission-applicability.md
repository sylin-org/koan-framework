---
type: SPEC
domain: framework
title: "R13-04 - Bind Native Admission to the Merge Candidate"
audience: [maintainers, provider-authors, release-engineers, ai-agents]
status: current
last_updated: 2026-07-21
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-21
  status: passed
  scope: declaration compiler, conservative planner, exact-SHA guard/executor, and always-emitted workflow
---

# R13-04 — Bind native admission to the merge candidate

- Status: `passed`
- Depends on: passed R13-03
- Unlocks: R13-05 terminal-outcome reconciliation
- Owner: optional durable claim-cell declarations plus the repurposed credential-free canary workflow

## Entry gate

**Application intent:** A maintainer always sees whether the exact proposed merge candidate required
native/provider proof and, when required, whether every declared cell passed on that candidate.

**Public expression:** A new or materially changed claim may declare exact admission cells with stable
ID, test project/filter, and `deterministic` or `native` lane. Opening a `main` pull request always
emits the native-admission check. No developer triggers publication or records run results in source.

**Guarantee/correction:** Applicability derives conservatively from affected active claim owners and
claim changes. The check records and verifies GitHub's exact merge-candidate SHA. It succeeds as
machine-derived N/A only when no affected claim declares a native cell; otherwise every required cell
must be present and Passed under R13-03. Missing/foreign-SHA results fail. The workflow carries no
NuGet or provider publication credential.

**Complete intent surface:** optional `product/claims.json` declarations, claims schema/models/compiler,
evaluated package paths and conservative shared-input changes, PR base/candidate commits, R13-03 result
contract, `canary-nightly.yml`, and branch-protected always-emitted check behavior.

**Public concepts:** Admission cell identity, lane kind, and exact commit SHA. These are evidence
identity/provenance, not run state or maturity.

**Coalescence:** Extend the existing claims source minimally because it already owns durable claim
evidence identity. Keep results in CI artifacts/checks, never claims. Reuse RepositoryInspector's Git
diff/evaluated graph facts without rebuilding release planning. Repurpose and delete the disabled
canary noop. ProductSurfaceCompiler validates declaration identity/existence but does not consume
result state.

**Ergonomics:** Unaffected PRs get one clear N/A result; affected native claims get exact cell names,
commands, and SHA. Reviewers never infer success from a skipped conditional job.

## Exact placement

| Change | Location | Reason |
|---|---|---|
| optional claim-cell model/schema validation | `product/claims.json`, packaging models/constants/compiler | existing durable claim evidence owner |
| affected-claim/native plan command | existing `Koan.Packaging` entry point and focused service | evaluated graph plus Git facts already live here |
| always-emitted credential-free job | `.github/workflows/canary-nightly.yml` | accepted disabled native-capable workflow candidate |
| functional parser/applicability/SHA tests | `tests/Koan.Packaging.Tests` | closest policy/compiler proof boundary |

## Stop conditions

- Stop if applicability can return N/A for an affected required cell.
- Stop if a result is not bound to the exact merge SHA or any required cell is skipped.
- Stop if claims store execution results or the workflow gains publication credentials.

## Implementation and evidence

- Claims may optionally declare stable lowercase admission-cell IDs with an existing test project,
  exact filter, deterministic/native lane, phase, and bounded deadline. The product compiler rejects
  duplicate IDs, missing/non-test projects, invalid lanes, and invalid deadlines; only declaration
  identity enters generated product truth.
- `NativeAdmissionPlanner` maps changed evaluated project directories through reverse public
  dependency closure, claim documentation/evidence/cell projects, and conservative shared
  build/tool/test-kit inputs. It selects only native cells from affected active claims.
- `native-admission` resolves both commits, requires the base to be an ancestor, requires `HEAD` to
  equal the supplied candidate, rejects any tracked or untracked checkout residue, and records the
  full candidate SHA in its plan/report.
- The executor runs every required cell through R13-03 and fails on any failed or missing result. No
  affected native cells emits `not-applicable` as an explicit successful report, not a skipped job.
- `canary-nightly.yml` is now the always-emitted `native-admission` main-PR workflow. It checks out
  full history without persisted credentials, passes GitHub's base and merge SHAs, and emits the
  exact-candidate JSON in the check log before returning its verdict. It has no secret, package push,
  publication, or dispatch.

## Acceptance result

- Outcome: PASS
- Date and commit: 2026-07-21; intentionally uncommitted local R13 slice
- Application intent and complete public expression: optional claim cell declarations plus one
  automatic main-PR check; no application or publication command changes
- Guarantee / correction: conservative applicability cannot return N/A for an affected declared
  native cell; foreign SHA, unrelated base, dirty checkout, failed result, and missing result fail
- Coalescence disposition: durable identity stays in claims; process/result truth stays in CI and
  reuses the R13-03 runner; the disabled canary noop is deleted
- Ergonomics proof: unaffected/no-native plans return one self-explaining `not-applicable` report
- Tests / validation: focused product/admission/native/compiler/workflow slice 52/52; Release tool
  builds with zero warnings/errors; real surface check remains 29 claims/93 packages/current
- Exact-candidate boundary: local dirty work is intentionally not certified; functional guard tests
  cover exact/foreign/unrelated/dirty candidates and the workflow supplies GitHub's merge SHA
- Unsupported scenarios: no source-stored run result, secret provider credential, manual dispatch,
  publication permission, or conditional skipped job exists
- Follow-up work: R13-05 reconciles every removed fixed-baseline owner before Wave 0 opens
- Reviewer: pending maintainer review
