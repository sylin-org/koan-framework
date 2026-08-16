---
name: koan-upgrade
description: Audit, plan, and migrate an older Koan application to current Koan. Use when an app resolves older Koan dependencies, uses removed Koan APIs, configuration, or composition, or the user requests a Koan framework-version migration. Preserve observable application contracts and stop when a current replacement cannot be proven. For same-version feature, provider, or data changes use koan; for no-change explanation use koan-explain.
---

# Koan Upgrade

Move an older Koan application to current Koan while preserving what its users and operators observe. Replace obsolete framework expression; do not redesign the application along the way.

## Scope

This skill owns Koan dependency, API, configuration, composition, and mixed-version migration.

It does not own provider changes, data transformation, or incidental changes to routes, payloads, security policy, tenant keys, database names, storage keys, or deployment topology. Hand those changes to `koan` unless the user explicitly includes them.

Use `koan-explain` when no migration has been requested.

## Safety boundary

- Establish the source version and the current target from evidence before editing.
- Preserve observable behavior unless the user explicitly changes a contract.
- Never infer a replacement from a similar name or an example from another version.
- Never migrate or delete persisted data, switch providers, or change topology without separate authorization.
- Preserve unrelated user changes; do not clean or reset a dirty working tree.
- Stop at an unknown required replacement. Report the evidence gap and the smallest decision needed.

Observable contracts include routes, payloads, status and error behavior, Entity IDs and query semantics, authentication and tenancy, provider routing and data names, jobs and events, MCP tools and resources, storage keys, and network exposure.

## Workflow

1. **Baseline.** Read repository instructions, project and dependency files, existing resolved assets, lock/facts/health output, source, configuration, tests, and deployment assets. Record the behavior and boundaries that must survive.
2. **Inventory.** List each old dependency, API, configuration key, composition call, and active instruction that may need change. When useful, run `scripts/inventory.ps1 -Path <project-root> -Format Json`; it only reads files.
3. **Map.** Prove each replacement with evidence matching the chosen current target: current dependency metadata, public docs, source, and focused tests. If evidence is absent or contradictory, mark the seam unknown.
4. **Change.** Migrate the smallest coherent area at a time: dependencies, bootstrap, Entities and data semantics, public/security surfaces, other capabilities, then tests and active instructions. Do not combine a provider or data move with the framework upgrade.
5. **Verify.** Compare before and after behavior, effective composition, and corrective failures. Run narrow checks first, then the repository's proportional gate.

Load [workflow.md](references/workflow.md) for the inventory table and stop rules, [legacy-map.md](references/legacy-map.md) when classifying old fingerprints, and [verification.md](references/verification.md) before claiming completion.

## Handoff

Lead with the older -> current result. Report what changed, which observable contracts stayed the same, checks run, any unproved or deferred seam, and whether data/provider/topology remained untouched.

Do not call the application fully upgraded while a required seam is unknown or unproved.
