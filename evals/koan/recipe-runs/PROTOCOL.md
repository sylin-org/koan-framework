# Recipe cold-run protocol

How to execute a recipe from `docs/recipes/` as an untrusted cold evaluator. The purpose of a
cold run is **fast, honest signal about the recipe** - not completing the task at any cost. A
blocker found in minute four is worth more than a workaround discovered in minute forty.

## Mindset

You are not the developer's hero. You are the recipe's test harness. Every time you resolve a
gap by consulting framework source, samples, or your own invention, you are hiding a defect in
the recipe. The next cold agent will hit the same wall without your patience.

## Mental model: Koan provisions on demand

The framework creates most of what it needs at first use. Reference the SQLite adapter and the
database file (`.koan/data/Koan.sqlite`) plus its schema appear on first run. Vectors index on
save; embedding width is measured from the first document; modules activate from package
references alone. **Do not pre-create databases, schemas, wiring, or configuration files "to be
safe"** - that is wasted time and usually wrong. Recipes call out the deliberate exceptions
(e.g., side-loaded ONNX model artifacts); treat everything else as on-demand and let the recipe's
verify block prove it.

## The stop rules

1. **STOP and report** the moment you hit a **BLOCKER** or **CONFUSING** obstacle:
   - BLOCKER - the step cannot be completed as written (compile error, crash, missing
     instruction, documented behavior that does not happen).
   - CONFUSING - you can proceed only by guessing, inventing structure, or consulting framework
     sources beyond the recipe and its linked exemplars.
2. **Continue past MINOR** items (typos, cosmetic wording, a command flag you already know).
3. **Do not** debug framework internals to unblock yourself. Do not read `src/` to discover
   namespaces, defaults, or wiring. Do not invent architecture. The blocker report is the
   deliverable.
4. **Exception:** if the whole journey completes with no BLOCKER/CONFUSING obstacles, finish
   everything and return the full evidence table.

## Blocker report format

```text
BLOCKED at step <n> "<step title>"
Recipe said: "<verbatim quote>"
Reality: <exact error message / observed behavior>
Tried: <what you attempted before stopping, briefly>
Obvious fix (if any): <one line, optional>
```

## Full-pass report format

- Evidence table: recipe claim -> pass/fail + captured actuals (exact strings, status codes,
  ranked results).
- MINOR list (numbered, one line each).
- Timing per phase.

## Ground rules

- Scratch only inside `tmp/<eval-name>/` (gitignored). Nothing outside the repo.
- No mutating git commands.
- Network allowed for NuGet restore and any cost the recipe itself declares.
- Capture actuals verbatim - "it worked" is not evidence.
