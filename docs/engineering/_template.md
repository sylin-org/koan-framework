---
type: DEV
domain: framework
title: "Operational workbook template"
audience: [maintainers, contributors]
status: current
last_updated: 2026-07-19
framework_version: v0.20.0
---

# [Workbook title — what task this covers, in plain English]

> **Replace this whole template with your workbook content.** Required sections are marked REQUIRED; recommended sections are marked OPTIONAL.
> Remove this header before publishing.
> Standard reference: [ARCH-0083](../decisions/ARCH-0083-operational-workbooks.md).

---

## When to use this *(REQUIRED)*

One paragraph. Who's reading this and why. What's in scope, what's NOT in scope (point at the right neighbor doc for out-of-scope).

**Prerequisites:**

- Anything the reader must have set up beforehand (tools, secrets, branches)
- Keep this short — link to a guide if setup is non-trivial

---

## Mental model (30 seconds) *(REQUIRED)*

The framing the rest of the workbook depends on. Two or three short paragraphs OR a small diagram. Keep it factual — no rationale, no history. The ADR carries the *why*; this carries the *what*.

If you find yourself writing more than ~150 words here, you're teaching, not framing. Move the rest to a guide.

---

## Happy path *(REQUIRED)*

The one canonical recipe. The thing 80% of readers do 80% of the time.

```pwsh
# Command-by-command. Brief inline comment for non-obvious steps.
some-command --with-flags
```

```pwsh
# Next command.
another-command
```

End with what success looks like: a specific file appearing, a specific log line, a green checkmark in some UI. The reader needs to know they're done.

---

## Scenarios *(OPTIONAL — include when the workbook covers more than the happy path)*

| If you want to... | Go to |
|---|---|
| Bump a single package without affecting others | [Scenario A](#scenario-a-...) |
| Roll back a release that went bad | [Failure → recovery](#failure--recovery) |
| Try a dry-run before committing | [Scenario B](#scenario-b-...) |

### Scenario A — [name it in plain English]

Brief context: when this applies. Then the commands. Same shape as Happy path, just for a different task.

### Scenario B — [...]

...

---

## Failure → recovery *(REQUIRED)*

For each known failure mode: the symptom, why it happens, the exact recovery commands. Symptoms are observable (a log line, a status, a file state) — not "you feel like something's wrong."

### Symptom: [observable thing the reader sees]

**Why it happens:** one sentence.

**Recovery:**

```pwsh
# Exact commands. The reader should not have to invent anything.
recovery-command-1
recovery-command-2
```

**Verify:** what success looks like after recovery.

### Symptom: [next failure mode]

...

---

## Anti-patterns *(OPTIONAL — include when there are real footguns)*

Things NOT to do, with the lesson behind each. Keep these short and concrete.

- **Don't [specific action]** — [lesson, one sentence: what goes wrong and why]
- **Don't [next]** — [...]

---

## References *(OPTIONAL — include when relevant)*

- `ARCH-XXXX — title` — replace with the decision this workbook operationalizes
- `scripts/path/to/Script.ps1` — replace with a link to the tool this workbook drives
- `sibling.md` — replace with a related workbook
