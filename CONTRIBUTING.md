# Contributing to Koan

Thank you for wanting to make Koan better. Koan is an opinionated framework — contributions
succeed when they work **with** those opinions, and the fastest way to a merged PR is understanding
them first.

## The laws of the tree

These are short, and they are load-bearing (full versions in [CLAUDE.md](CLAUDE.md) and
[docs/MEMORY.md](docs/MEMORY.md)):

- **A package reference is the intent.** Capabilities activate by being referenced; `AddKoan()`
  composes them. No manual registration, no service locators, no per-app provider wiring.
- **Application code states business meaning.** Framework pillars own composition, provider
  election, lifecycle, and explanation.
- **Never construct an identifier from a product name.** Package and API names are exact; copy
  them from the [capability map](docs/reference/capability-map.md).
- **Verify empirically.** Probe the real store, read startup facts, run the thing. Confident
  claims that one command would have caught are the classic failure here.
- **Root fix, not spot fix.** Repair the owner; don't drop a capability to a floor to make a
  suite green.
- **Never hand-edit a package version.** Versions come from the per-project `version.json`
  (NBGV); releases fast-forward `main` from `dev`.

## Getting started

1. Fork, branch from `dev`.
2. `dotnet build Koan.sln` — one build at a time; the repo builds all projects into a shared
   output path, so parallel builds clobber each other.
3. Run the suite that owns the project you changed (see `tests/Suites/`), not just the ones that
   use it.
4. Before changing production code, read the relevant ADR in `docs/decisions/` — decisions are
   dated records, and a later one supersedes; don't re-litigate without new evidence.

## Pull requests

- One concern per PR; keep the diff reviewable.
- Behavior changes need a spec in the owning suite and, when policy changes, a doc touch in the
  same PR.
- Corrective errors are part of the API: a refusal should name what to do next.
- CI runs `PR gate`; releases are fast-forwards of `main` and publish only what changed.

## Reporting issues

Include: what you expected, what happened, the resolved `Sylin.*` package versions
(`dotnet list package`), and the composition facts if runtime behavior is involved
(`/.well-known/Koan/facts`). A minimal reproduction beats a long description.

## Questions

See [SUPPORT.md](SUPPORT.md).
