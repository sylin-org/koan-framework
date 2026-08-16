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

Two defects were caught the first time this ran: a job handler signature that did not exist, and a
canonical bootstrap snippet missing its `using` directive. Both had passed every pattern-based check.

Add a journey whenever the skill starts teaching something a compiler could disprove.
