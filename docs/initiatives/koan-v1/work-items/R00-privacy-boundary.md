---
type: GUIDE
domain: framework
title: "R00 - Establish the Privacy Boundary"
audience: [maintainers, ai-agents]
status: draft
last_updated: 2026-07-13
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-13
  status: reviewed
  scope: privacy audit work-item specification
---

# R00 — Establish the privacy boundary

- Tranche: `T0 — privacy and memory boundary`
- Status: `passed`
- Depends on: none
- Unlocks: R01
- Owner: maintainer

## Meaningful outcome

The public Koan repository can use real-application learning without exposing or depending on any
private downstream identity. A new session has a safe, repeatable audit procedure and knows where
operator approval is required.

## Why now

The initiative cannot responsibly create public positioning, examples, or evidence until its source
boundary is explicit. The current tree contained one identifying token, while repository history has
not yet been conclusively assessed.

## Evidence to read first

- [`../CHARTER.md`](../CHARTER.md), especially the private downstream boundary.
- [`../PROGRESS.md`](../PROGRESS.md), including the divergence log.
- Current tracked files and published Git history, using terms supplied privately by the operator.

## Decisions

### DECIDED

- Public artifacts use only `private downstream application` or `private downstream evidence`.
- No public file records an application name, owner identity, local path, distinctive business domain,
  private search term, or other identifying clue.
- Downstream experience creates questions; repository-owned artifacts create evidence.

### DEFAULT

- Remediate the current tree normally.
- Preserve history unless an audit finds exposure and the operator explicitly chooses remediation.

### OPEN

- Does published history contain an identifying reference?
- If it does, is removal proportionate to the exposure and coordination cost?

## Scope

### In

- Sanitize known current-tree references.
- Audit tracked content, tracked paths, and reachable history with an out-of-band term list.
- Record only result categories, counts, and an operator decision.
- Define the anonymous evidence-conversion rule.

### Out

- Publishing a downstream case study.
- Copying private code or configuration into Koan.
- Rewriting history without a separate operator decision.

## Safe audit procedure

1. Store the term list outside the repository in an operator-controlled local file. Do not print it,
   attach it, or place it in shell history.
2. Check tracked file contents and paths. Capture counts in memory; do not emit matching lines or names
   into logs.
3. Search reachable commits in bounded batches. Record the commit-count coverage and match count, not
   terms, paths, snippets, or identities.
4. Inspect any match locally and sanitize the current tree with generic language.
5. If a match exists only in history, stop and record `operator decision required` in `PROGRESS.md`.
6. If a rewrite is approved later, plan notification, backup, force-push coordination, and verification
   as a separate operator-owned action.

Do not improvise a public CI privacy list: publishing the detector vocabulary would itself disclose the
boundary it is meant to protect.

## Verification

- Current tracked content count for the private term set: zero.
- Current tracked path count for the private term set: zero.
- Reachable history coverage and result recorded without identifying detail.
- Documentation lint and `git diff --check` pass.
- A manual diff review finds no private name, path, domain detail, or identifying example.

## Acceptance additions

- The history result is `clean`, `exposure found; operator decision recorded`, or `inconclusive;
  bounded restart documented`.
- If inconclusive, R00 remains `in-progress` or `blocked`; it cannot pass on assumption.

## Stop conditions

- Stop before printing a match into any durable session output.
- Stop before changing published history.
- Stop if the term list cannot be kept out of repository artifacts.

## Session close

Update [`../PROGRESS.md`](../PROGRESS.md), replace [`../NOW.md`](../NOW.md), and apply
[`../ACCEPTANCE.md`](../ACCEPTANCE.md).

## Acceptance result

- Outcome: PASS
- Date and commit: 2026-07-13; recorded by the R00 closure commit
- Evidence: all three published branch tips contain no private audit-term matches; current tracked
  content and paths are clean; the public initiative contains only the generic downstream boundary.
- Tests / validation: scoped documentation lint, TOC parsing, private current-tree scan, branch-tip
  scans, and `git diff --check`.
- Unsupported scenarios: historical commits, GitHub-managed pull refs, caches, forks, and existing
  clones were not rewritten or claimed clean.
- Follow-up work: none unless concrete discoverability or personal-risk evidence justifies a separately
  approved GitHub-coordinated purge.
- Reviewer: operator-approved forward-only disposition
