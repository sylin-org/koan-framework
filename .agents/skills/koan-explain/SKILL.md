---
name: koan-explain
description: Explain a Koan application from local evidence without changing files or state. Use when asking what an app does, how Entities, routes, jobs, AI, or MCP compose, why a provider or route was selected, what facts, health, or koan.lock.json mean, where behavior lives, or why observed runtime state or failure occurs. This skill is strictly read-only. For changes use koan; for moving an older Koan app to current Koan use koan-upgrade.
---

# Koan Explain

Explain what the application means and why it behaves that way. Keep the answer grounded in the application, concise, and entirely read-only.

## Read-only boundary

You may list, search, and read repository files; inspect existing project metadata, source, tests, build artifacts, lockfiles, logs, facts, and health output; and query an already-running documented read-only endpoint when access is already authorized.

Do not edit, create, delete, format, generate, install, restore, build, start, stop, migrate, deploy, or otherwise change files, processes, configuration, data, or external state. Do not make a small fix to prove an explanation.

If the request also asks for changes, complete the explanation first and hand implementation to `koan` or a framework-version migration to `koan-upgrade`.

## Ask the application first

A Koan application describes itself. Prefer its own account over inference from source:

| Question | Where it answers |
|---|---|
| Is the process alive? | `/health/live` |
| Are required dependencies ready, and which one is not? | `/health/ready` |
| What composed, which provider won, and why? | `/.well-known/Koan/facts` · `koan://facts` |
| What is agent-visible, and under what rules? | `koan://entities` · `koan://self` |
| What did references compose, and has it drifted? | `koan.lock.json` |

Facts and health are redacted projections of the same runtime decisions, so quoting a provider ID or
setting name from them is safe; quoting a connection string is not.

These answer for **one run**. A captured response explains the run that produced it, not the
repository in general. If no such output exists, say so — do not start the application to create it.

## Workflow

1. Restate the question in application terms.
2. Read repository instructions and the smallest local evidence set that can answer it.
3. Trace the relevant path: public doorway -> application owner -> capability or route -> provider -> result or failure.
4. Separate declared intent from effective runtime selection.
5. Mark material conclusions **Observed**, **Inferred**, or **Unknown**.
6. Lead with the answer, cite decisive local evidence, and name one read-only next check only when useful.

Load [evidence.md](references/evidence.md) for evidence selection and uncertainty rules. Load [output-contract.md](references/output-contract.md) only when the explanation spans several owners or claims.

## Interpretation rules

- Project references show available intent, not which provider won or whether the capability works.
- Application source and tests show owned behavior; configuration shows declared intent.
- Existing facts, health, lock, and captured runtime output show effective composition for that run.
- Framework source or docs can explain a mechanism only when they match the application's resolved version.
- Samples and design notes provide context, not proof of this application's behavior.
- When evidence conflicts or a version match cannot be established without writing, say **Unknown**.
- Redact secrets and sensitive Entity values; names of settings and providers are usually enough.

For an older application, explain only behavior observed in its own evidence. Do not present old APIs as current guidance. Route requested modernization to `koan-upgrade`.

## Broad answer shape

For "explain this app," cover only what matters:

1. **Purpose** - the user or operator outcome.
2. **Application vocabulary** - key Entities and operations, with their owners.
3. **Doorways and flow** - HTTP, MCP, jobs, events, storage, or other participating surfaces.
4. **Composition** - selected routes/providers and the evidence for them.
5. **Guarantees and edges** - what remains true and what is unknown or externally owned.

Use a table or flow only when it makes three or more relationships easier to understand. Do not narrate every file.

## Completion check

The developer should know what the app does, why its pieces participate, which conclusions are proved, and what remains unknown. The working tree and external state must be unchanged.
