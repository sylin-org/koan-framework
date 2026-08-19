# KoanConsoleApp

A persisted Todo console application: start Koan, save an Entity, load it, and query business state.

## Run

```powershell
dotnet run
```

The result includes the saved identity, the loaded title, and the open-Todo query:

```text
saved: 01J...
loaded: buy milk
open todos:
  - buy milk
```

## Read the application

| File | Business meaning |
|---|---|
| `Todo.cs` | the state the application owns |
| `Program.cs` | host lifecycle plus save/get/query intent |
| `KoanConsoleApp.csproj` | choose the Koan foundation and durable embedded SQLite provider |
| `AGENTS.md` | orient any coding agent to this application |
| `.claude/settings.json` | make Koan's coding skills available to an agent working here |

`AGENTS.md` is the portable one. It states the two rules that shorten most work, names the evidence
this application produces about itself, and links the capability map that turns "add AI to this" into
an actionable request. It names no capability, so it does not go stale as Koan grows.

`.claude/settings.json` registers Koan's plugin marketplace and enables the `koan` plugin, so a coding
agent gains the `koan`, `koan-explain`, and `koan-upgrade` skills once you trust the folder. The skills
are referenced rather than copied, so they follow the framework. Delete either file to opt out.

SQLite is elected from the package reference and defaults to `.koan/data/Koan.sqlite`. No provider registration,
connection setting, schema script, or repository is required. Startup output explains the resulting composition.

Add a property or another Entity and run again. To move backends, reference the intended provider and configure only
the endpoint or credentials it cannot derive; the business code does not change.

SQLite is durable embedded storage, not a multi-node service or complete production backup strategy. Application
policy, security, validation, and deployment remain explicit decisions.
