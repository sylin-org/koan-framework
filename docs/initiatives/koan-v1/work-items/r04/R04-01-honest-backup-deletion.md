---
type: GUIDE
domain: backup
title: "R04-01 - Make Backup Deletion Honest"
audience: [maintainers, framework-authors, ai-agents]
status: draft
last_updated: 2026-07-13
framework_version: v0.17.0
---

# R04-01 — Make backup deletion honest

- Priority: P0
- Status: `passed`
- Depends on: R03
- Owner: Data.Backup

## User-visible failure

At card creation, `entity.DeleteBackup(name)` returned `true` without deleting anything. An application,
operator, or agent could make a destructive request, receive success, and retain the data.

## Personas

- Developer: cannot trust a boolean success result.
- Agent: may report task completion from a fabricated success.
- Operator/reviewer: cannot prove deletion or remediation from logs/tests.

## Current evidence

- `src/Koan.Data.Backup/Extensions/EntityBackupExtensions.cs` contains an explicit placeholder and
  unconditional success.
- R03 classifies backup catalog/deletion as a type/control-plane concern, not an instance-local verb.

## Smallest meaningful fix

Fail with one stable, actionable Koan error until a real deletion service and receipt contract exist.
Add focused tests proving no success is returned and no unrelated backup API regresses. Do not design
the eventual backup control plane in this card.

## Failure behavior

The error names backup deletion as unsupported, states that no deletion occurred, and directs callers
to a safe supported action. It contains no storage paths or secrets.

## Verification

- focused Data.Backup tests cover the extension and any static equivalent;
- the operation never returns `true` without repository-owned deletion evidence;
- build and existing backup tests pass;
- public reference docs do not advertise working deletion.

## Compatibility and rollback

Behavior changes from false success to explicit failure. This is a safety repair, not a supported
compatibility guarantee. Keep the method temporarily if removal would obscure the correction; mark
replacement/removal only when a real control-plane design is approved. Rollback is reverting the
single guard/test change, but false success is not an acceptable steady state.

## Stop condition

If a complete deletion implementation requires storage-provider or catalog redesign, stop at the
fail-loud repair and create a later capability card.

## Completion evidence

- `EntityBackupExtensions.DeleteBackup(...)` now returns a faulted `Task<bool>` with
  `NotSupportedException`; it resolves no host service, touches no storage, and cannot return success.
- The exact error states that deletion is unsupported, confirms that nothing was deleted, and directs
  the caller to retain the backup until a verified management operation exists.
- `Koan.Data.Backup.Tests` is registered in `Koan.sln`; its self-executing xUnit v3 run reports one
  passing test with no failures or skips.
- The Data.Backup module and test project build with zero errors. Existing warnings are unrelated
  package-pruning warnings in the module dependency graph.
- No static deletion equivalent exists. The broader backup control plane remains deliberately deferred.
