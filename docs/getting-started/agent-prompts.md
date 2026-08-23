---
type: GUIDE
domain: agents
title: "Get started with Koan agent prompts"
audience: [developers, ai-agents]
status: current
last_updated: 2026-08-22
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-22
  status: passed
  scope: skill activation paths (Claude Code plugin, Codex, Cursor, Gemini CLI), recipe-index routing, and the semantic-search walkthrough against a fresh koan-web template
---

# Get started with Koan agent prompts

Your generated project already knows how to talk to coding agents. This page shows how to use
that: one sentence of intent in, a verified capability out.

The mechanism is a set of [agent skills](../guides/agent-skills.md) - `koan`, `koan-explain`, and
`koan-upgrade` - written in the open SKILL.md format that Claude Code, Codex CLI, Cursor, Gemini
CLI, GitHub Copilot, OpenCode, and dozens of other harnesses read natively. The skills carry
Koan's grammar, the exact package identifier for every capability, per-capability recipes, and a
proof discipline: your agent does not claim a feature works, it shows you.

## Before your first prompt

You need a Koan application and an agent harness that has the skills.

```powershell
dotnet new install Sylin.Koan.Templates
dotnet new koan-web -o TodoApi
cd TodoApi
```

Template projects ship pre-wired: `.claude/settings.json` already registers Koan's marketplace,
enables the skills, and allows read-only fetches of the capability docs. Opening the folder in
Claude Code offers the plugin on first launch; accept it once.

Any other harness - one command from anywhere:

```powershell
npx skills add sylin-org/koan-framework
```

That installer targets 50+ agents (Codex, Cursor, Gemini CLI, Copilot, Windsurf, OpenCode...).
Harnesses with no skills support still get the essentials: every Koan repository ships an
`AGENTS.md` that routes them to the same rules, and `llms.txt` indexes the documentation.

## Your first sentence

With the app folder open in your agent:

```text
$koan add semantic search for Todo
```

(In Cursor or Gemini CLI you can usually just say "add semantic search to my todos" - the skill's
description activates it implicitly.)

What happens next, in order:

1. **Routing.** The skill recognizes an outcome request and reads Koan's recipe index, which
   routes "search by meaning" to one recipe page holding the exact packages, configuration keys,
   working code, and provider limits.
2. **References.** Three packages join (`Sylin.Koan.AI.Connector.Onnx`, `.SqliteVec`,
   `Sylin.Koan.Data.AI`) - copied verbatim from the map, never invented.
3. **Code.** One attribute on the entity, one method on the controller you already have:

```csharp
[Embedding(Template = "{Title}. {Notes}")]
public sealed class Todo : Entity<Todo> { /* ... */ }

[HttpGet("search")]
public async Task<IActionResult> Find(string q, CancellationToken ct)
    => Ok(await Vector<Todo>.Search(await Koan.AI.Client.Embed(q, ct), topK: 10));
```

4. **Proof.** The skill requires evidence over assertion. Expect the agent to build, start the
   application, read `/.well-known/Koan/facts` to show the embedder and vector store were elected,
   POST a few todos, and run the search endpoint before calling the job done.

First ONNX use downloads a local model file; everything runs on your machine after that.

## Sentences that work

Same pattern, different capabilities. Each row is a real route through the recipe index.

| You say | You get |
|---|---|
| `$koan add photo uploads, and give me a little gallery page` | Entity-owned originals in storage, resize recipes served over HTTP, an embedded static gallery - the frontend topology chosen deliberately |
| `$koan add soft delete to Todo` | Recoverable deletion: hidden removal, recycle bin, restore and purge verbs |
| `$koan add background cleanup that runs nightly` | A job entity owning its own execution - durable, retried, inspectable |
| `$koan let Claude work these todos` | MCP tools over the same entities, under the same access rules as HTTP |
| `$koan make this multi-tenant for acme and globex` | Ambient tenant segmentation across data, cache, storage and events |

Prefer typing the changes yourself? Every diff the agent produces is also the entire manual
change - the recipes are public pages linked from each capability row in the
[capability map](https://github.com/sylin-org/koan-framework/blob/main/docs/reference/capability-map.md).

## How it stays honest

- Package identifiers are copied from the live capability map, never constructed from a product
  name. On restricted networks the skill falls back to dated snapshots bundled inside itself and
  tells you which date it used.
- Claims are graded. The generated [product surface](https://github.com/sylin-org/koan-framework/blob/main/docs/reference/product-surface.md)
  marks every capability supported, demonstrated, or unassessed, and the skills pass those limits
  through instead of promising parity.
- Composition is checkable afterwards by anyone: `/.well-known/Koan/facts`,
  `/health/ready`, and the checked-in `koan.lock.json`.

## Boundaries

Koan's AI connectors are local-first (Ollama, LM Studio, in-process ONNX); hosted frontier-model
connectors do not exist yet. Capabilities marked unassessed carry no guarantees. The agent writes
your business logic; you review small diffs in business language - that division of labor is the
point.
