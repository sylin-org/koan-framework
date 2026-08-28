---
type: GUIDE
domain: framework
title: "A07 - Distribution registrations"
audience: [maintainers, ai-agents]
status: draft
last_updated: 2026-08-27
framework_version: v1.0.12
validation:
  date_last_tested: 2026-08-27
  status: reviewed
  scope: distribution work-item specification
---

# A07 — Distribution registrations

- Tranche: `T2 — Launch`
- Status: `draft`
- Depends on: A03 (listings want a demo link)
- Unlocks: A09
- Owner: maintainer

## Meaningful outcome

Koan is registered, with accurate descriptions and the demo link, on the free high-intent surfaces:
MCP directories (Smithery, Glama, and the MCP registry) carrying the agent-surface story;
awesome-dotnet / awesome-mcp list PRs; refreshed NuGet package READMEs so NuGet search lands on a
working quickstart; `llms.txt` submitted to aggregation services that index it.

## Why now

The pre-announcement baseline shows zero inbound referrers: discovery currently happens only by
accident on NuGet. These registrations are permanent, free, and reach exactly the audiences the
charter ranks.

## Content contract

- Each listing's description is checked against ACCEPTANCE §0/§1 before submission.
- Every external action (a registration or PR) is recorded in `PROGRESS.md` with its URL —
  externally visible actions need the recorded operator decision ACCEPTANCE §3 requires.
- Registrations that request maintenance (list PRs) record the follow-up cost honestly.

## Acceptance criteria

- [ ] MCP directory entries live or submitted; awesome-list PRs opened.
- [ ] NuGet READMEs for `Sylin.Koan`, `Sylin.Koan.App`, `Sylin.Koan.Templates` carry the current
      quickstart and demo link.
- [ ] Registry of submissions with URLs in `PROGRESS.md`.

## Proof

Submission URL list in `PROGRESS.md`.
