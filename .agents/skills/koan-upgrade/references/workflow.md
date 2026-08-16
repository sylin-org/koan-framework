# Upgrade workflow

## Baseline

Before editing, record:

- user-owned working-tree changes;
- source and target Koan versions;
- project dependencies and target framework;
- existing lock, facts, health, and representative behavior;
- routes, payloads, tools, resources, provider routes, data names, identity, tenancy, and topology that must remain stable;
- the narrow build and test commands for proof.

Do not clean or reset unrelated work.

## Track each seam

| Item | Old evidence | Current evidence | Change | Preserved behavior | Proof | Status |
|---|---|---|---|---|---|---|
| dependency, API, configuration, composition, or instruction | exact path/version | matching target evidence | smallest verified change | observable contract | focused check | known, unknown, or done |

Inventory dependencies, bootstrap calls, Entities and queries, provider routing, Web and serialization, auth and tenancy, jobs/events/storage/media, AI/vector/MCP/Canon, configuration, operational evidence, tests, and active instructions only when they occur in the application.

## Resolve replacements

Use evidence that matches the chosen current target: resolved dependency metadata, public docs, source, and focused tests. Application examples can demonstrate a shape but cannot prove that it replaces an older seam.

Do not guess from names, copy from another version, or preserve obsolete ceremony without an application reason.

## Stop rule

Stop a required seam when:

- current evidence is absent or contradictory;
- behavior cannot be preserved automatically;
- the change requires a public, security, data, provider, or topology decision the user did not authorize.

Report the smallest missing evidence or decision. Continue independent known work only when the application remains coherent and recoverable.
