# AI Engine facade

Sora exposes a terse, SoC-aligned facade for AI operations under `Sora.AI.Engine`.
It complements the existing `Sora.AI.Ai` facade with clearer naming and simple provider/model targeting.

## Why

- Clear mental model: an "engine" executes prompts/embeddings.
- Terse usage without DI noise.
- Optional selection of provider/model inline for explicit routing.

## Usage

- Default engine (app-configured):
  - `if (Sora.AI.Engine.IsAvailable) { var emb = await Sora.AI.Engine.Embed(req, ct); }`
  - `var text = await Sora.AI.Engine.Prompt("Write a haiku", model: "small", ct);`
- Explicit provider/model:
  - `var emb = await Sora.AI.Engine("ollama").Embed("text", model: "nomic-embed-text", ct);`
  - `var text = await Sora.AI.Engine("ollama", "llama3:instruct").Prompt("explain...", ct);`

## Behavior

- Provider/model selectors override app defaults.
- Falls back to app-configured routing when not specified.
- `IsAvailable`/`Try()` allow graceful feature toggles.

## Status

- Accepted design; implemented as a thin alias over `Sora.AI.Ai` for backward compatibility.
- Samples (S5) updated progressively to prefer `Engine` over `Ai`.
