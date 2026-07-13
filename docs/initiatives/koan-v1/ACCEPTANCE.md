---
type: SPEC
domain: framework
title: "Koan V1 Reorganization Acceptance Gate"
audience: [architects, maintainers, ai-agents]
status: draft
last_updated: 2026-07-13
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-13
  status: reviewed
  scope: initiative work-item acceptance criteria
---

# Koan V1 Reorganization Acceptance Gate

Apply this gate to every work item. A card may add stricter criteria but may not waive privacy,
evidence, or truthful-support requirements.

## Decision outcomes

- `PASS` — every applicable criterion has linked evidence; update the work item to `passed`.
- `BLOCK` — the outcome remains valuable, but a named dependency or decision prevents completion;
  record the blocker and a safe restart point.
- `STOP` — evidence invalidates the approach, the work duplicates another owner, or continuing would
  violate an invariant; preserve the learning and close the card as `stopped`.

## 0. Privacy and provenance — mandatory

- No private downstream name, path, identity, distinctive domain detail, or identifying example is
  present in public artifacts.
- Private experience is treated as a source of questions, not public proof.
- Every public claim links to repository-owned code, tests, samples, documentation, or an explicit
  decision record.
- Destructive or externally visible remediation has a recorded operator decision.

Failure in this section is `BLOCK`, regardless of the rest of the scorecard.

## 1. Meaningful outcome and business density

- The user-visible result is stated before implementation detail.
- Each step leaves a coherent, useful application state rather than scaffolding debt.
- The common-path application code reads primarily as business language.
- Ceremony removed from the application is owned, explained, and diagnosable by Koan rather than
  merely hidden.
- Before/after evidence makes the improvement observable.

## 2. Entity semantics and discovery

Apply when a work item changes public application-facing behavior:

- `Entity<T>` remains the first place developers look for entity-centered capability.
- Relevant module capabilities are discoverable through IntelliSense without a separate service
  locator vocabulary.
- Extension methods preserve a coherent semantic model and avoid ambiguous or surprising overloads.
- Advanced escape hatches do not burden the common path.

## 3. Mechanical quality

- Focused automated tests cover the contract and important failure modes.
- Broader affected test suites pass in proportion to risk.
- Packages, samples, and documentation use coherent versions and supported entry points.
- Documentation lint, links, examples, and `git diff --check` pass where applicable.
- No unrelated user-owned changes are overwritten.

For documentation-only cards, runtime builds and tests may be marked not applicable with a reason;
documentation and diff validation remain required.

## 4. Errors, startup, and inspectability

- Startup can report what was discovered, selected, configured, defaulted, and rejected.
- Backend negotiation is deterministic, and selection or fallback can be explained.
- Failure messages state the failed intent, relevant capability/provider, and a safe corrective action.
- Secrets and private values are redacted.
- Developers, coding agents, operators, and reviewers can inspect the same underlying facts in suitable
  forms.

## 5. Architecture and ecosystem fit

- The change has one clear owner and respects package/layer boundaries.
- `Reference = Intent`, `Entity = Language`, `IntelliSense = Discovery`, and `Startup = Explanation`
  are preserved or an explicit decision explains why not.
- External-framework research identifies whether Koan should adopt, adapt, integrate, complement, or
  decline each relevant idea; similarity alone is not a reason to copy.
- New abstractions earn their cognitive and maintenance cost.

## 6. Support, upgrade, and removal

- Supported and unsupported scenarios are explicit.
- Compatibility expectations are named.
- Deprecation includes a replacement and removal condition.
- Migration from the previous common path is proportionate and documented.
- The capability ledger and public claims are updated together when maturity changes.

## Completion record

Add this block to the work item when closing it:

```markdown
## Acceptance result

- Outcome: PASS | BLOCK | STOP
- Date and commit:
- Evidence:
- Tests / validation:
- Unsupported scenarios:
- Follow-up work:
- Reviewer:
```
