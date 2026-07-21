---
type: DEV
domain: framework
title: "Koan operational workbooks"
audience: [maintainers, contributors]
status: current
last_updated: 2026-07-19
framework_version: v0.20.0
---

# Koan workbooks

Operational runbooks for day-to-day tasks. **When you have a goal and want to know exactly what to run — start here.**

If you want to understand *why* the system is shaped the way it is, you want an [ADR](../decisions/). If you want to learn a concept from first principles, you want a [guide](../guides/). Workbooks are for executing.

> **For the standard** these workbooks follow — required sections, naming conventions, anti-patterns — see [ARCH-0083](../decisions/ARCH-0083-operational-workbooks.md).

---

## How to use a workbook

Each workbook has the same shape:

1. **When to use this** — Is this the right doc for your task? Read this first.
2. **Mental model (30 seconds)** — The framing the rest depends on. Skim if you're new; skip if you've been here before.
3. **Happy path** — The canonical recipe. Copy-paste commands. Most reads end here.
4. **Scenarios** — A lookup table: *if you want X, jump to section Y*.
5. **Failure → recovery** — When something goes wrong, find the symptom, run the recovery commands. No detective work required.
6. **Anti-patterns** — Things not to do, with the lesson behind each.
7. **References** — Links to the ADR, scripts, and related workbooks.

The voice is imperative. Commands are concrete. Explanations sit alongside what they explain — never above or after a wall of theory.

---

## Active workbooks

| Workbook | What it covers |
|---|---|
| [versioning.md](versioning.md) | How per-package NBGV versions follow Git and how to express major/minor intent |
| [packaging.md](packaging.md) | The evaluated package contract, bundle shape, and local release proof |
| [nuget-publishing.md](nuget-publishing.md) | What every advancement of `dev` publishes and how failed events converge |
| [adding-a-connector.md](adding-a-connector.md) | Scaffolding a new connector (data store, vector store, AI provider, broker, storage) from csproj to integration tests |

---

## Contributing a new workbook

1. **Start from the template**: [`_template.md`](_template.md)
2. **Pick a name**: noun or noun phrase, lowercase, hyphenated, no `how-to-` prefix
   - ✅ `adding-a-pillar.md`, `data-migration.md`
   - ❌ `how-to-add-a-pillar.md`, `migration-process.md`
3. **Write to the audience** — operators mid-task, possibly half-asleep. Concrete commands, brief explanations.
4. **Required sections**: front matter, mental model, happy path, failure → recovery. Skip optional sections if they don't apply.
5. **Cross-link**: every operational script your workbook describes should mention the workbook in its header. Discoverability matters.
6. **Add to this index**: append a row to the table above.

Before sending the PR, read your workbook end-to-end imagining you've never seen the system. If any step assumes knowledge that isn't on the page or one link away, fix it.
