# Cold run: docs/recipes/poc-an-idea.md

Execute the POC recipe as a cold evaluator under [PROTOCOL.md](PROTOCOL.md) - stop-at-first
BLOCKER/CONFUSING, blocker report as the deliverable, full evidence table only on a clean pass.

## Run parameters

- Recipe: `docs/recipes/poc-an-idea.md` (read it first; follow literally where possible)
- Scratch root: `tmp/recipe-run-poc-an-idea/`
- App port: `http://localhost:5099`
- Feed note: the public NuGet feed must serve `Sylin.Koan.Data.AI >= 1.0.9` (derived vector
  space). Record the restored version from the csproj or `obj/project.assets.json`.

## Prior frictions to re-test explicitly

These were fixed after earlier runs; confirm each with evidence rather than assuming:

- F1 - `[Embedding]` attribute and attribute-only saves (no AddKoan lambda) compile and work.
- F3 - the recipe's entity file block is complete enough that no using had to be guessed.
- F4 - the seeding bridge (StartAsync -> AppHost.PushScope -> WaitForShutdownAsync) is stated
  clearly enough to use without invention.
- N1 - the ONNX side-load step (assets copy, `Koan:Ai:Onnx` config, Content copy-to-output) is
  stated clearly enough to use without invention.
- N3 - the search-controller exemplar link is sufficient to produce the endpoint.

## Deliverables

Per PROTOCOL.md: blocker report on early stop; otherwise evidence table + MINOR list + timing,
plus an explicit F1/F3/F4/N1/N3 line each (resolved / still broken, with receipt).
