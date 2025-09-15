# AI Engine facade

Koan exposes a terse, SoC-aligned facade for AI operations under `Koan.AI.Engine`.
It complements the existing `Koan.AI.Ai` facade with clearer naming and simple provider/model targeting.

## Why

- Clear mental model: an "engine" executes prompts/embeddings.
- Terse usage without DI noise.
- Optional selection of provider/model inline for explicit routing.

## Usage

- Default engine (app-configured):
  - `if (Koan.AI.Engine.IsAvailable) { var emb = await Koan.AI.Engine.Embed(req, ct); }`
  - `var text = await Koan.AI.Engine.Prompt("Write a haiku", model: "small", ct);`
- Explicit provider/model:
  - `var emb = await Koan.AI.Engine("ollama").Embed("text", model: "nomic-embed-text", ct);`
  - `var text = await Koan.AI.Engine("ollama", "llama3:instruct").Prompt("explain...", ct);`

## Behavior

- Provider/model selectors override app defaults.
- Falls back to app-configured routing when not specified.
- `IsAvailable`/`Try()` allow graceful feature toggles.

## Status

- Accepted design; implemented as a thin alias over `Koan.AI.Ai` for backward compatibility.
- Samples (S5) updated progressively to prefer `Engine` over `Ai`.
