# Skill journeys

Each directory is a small application that compiles against the **published** Koan packages, never
through a repository `ProjectReference`. That distinction is the point: the skill's audience installs
Koan from NuGet, so the oracle that checks the skill must consume it the same way. A journey building
against repository sources would pass while the guidance it verifies was unusable.

Run them with:

```powershell
pwsh scripts/skills-verify.ps1
```

Each journey isolates itself from the repository's build with empty `Directory.Build.props` /
`Directory.Build.targets` and `ManagePackageVersionsCentrally=false`, and takes its package version
from `KoanEvalPackageVersion`.

## What a journey is for

A journey exists to make a claim in the skill falsifiable by the compiler:

| Journey | Falsifies |
|---|---|
| `grammar` | Every construct the main skill teaches — the bootstrap, Entity operations, scoped context switches, the HTTP projection, durable work, embeddings, the agent surface, and model calls |

A journey catches what pattern matching cannot: a signature that does not exist, a snippet missing a
`using`, an identifier that no longer resolves. Add one whenever the skill starts teaching something
a compiler could disprove.
