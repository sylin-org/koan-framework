# Koan.AI.Prompt

Uri-inspired prompt primitives for Koan: parse, create, version, and store prompts with variable extraction, few-shot examples, output schema, and A/B testing support.

- Target framework: net10.0
- License: Apache-2.0
- Version: 0.6.3

## Install

```powershell
dotnet add package Sylin.Koan.AI.Prompt
```

## Quick Start

```csharp
// Build a prompt
var prompt = Prompt.Create()
    .System("You are a technical writer.")
    .Instruct("Summarise the following article in {{tone}} tone.")
    .Constrain("Maximum 200 words.")
    .OutputAs<SummaryOutput>()         // JSON schema from type
    .Example("long article...", "Short summary.")
    .Default("tone", "professional");

// Resolve with variables
var resolved = prompt.Resolve(new { tone = "casual" });

// Send to AI
var result = await Client.ChatAsync(resolved.SystemPrompt, resolved.UserMessage, ct);
```

## `Prompt` API

```csharp
// Fluent builder
Prompt.Create()
    .System(string systemPrompt)
    .Instruct(string template)             // Supports {{variable}} interpolation
    .Constrain(string constraint)
    .OutputAs<TOutput>()                   // Extract JSON schema from type
    .OutputAs(OutputSpec spec)             // Or provide explicit spec
    .Example(string input, string output)  // Few-shot example
    .Default(string variable, string value)// Default variable value
    .Meta(string key, string value)        // Arbitrary metadata

// Loading from storage
Prompt.Load("my-prompt")                   // Latest version
Prompt.Load("my-prompt", version: "v3")    // Specific version
Prompt.Load("my-prompt", PromptStrategy.AbTest(["v2", "v3"])) // A/B test

// Instance methods
prompt.Resolve(object variables)           // → string (resolved template)
prompt.UnresolvedVariables()               // → string[] (missing variables)
prompt.With(string variable, string value) // → new Prompt (immutable update)
```

## Variable Interpolation

Templates use `{{variable}}` syntax:

```csharp
var prompt = Prompt.Create()
    .Instruct("Translate the following to {{language}}: {{text}}");

var resolved = prompt.Resolve(new { language = "French", text = "Hello world" });
// → "Translate the following to French: Hello world"
```

## Prompt Storage

`PromptEntry` is a standard `Entity<PromptEntry>` — prompts are versioned and stored in the configured data backend automatically:

```csharp
// Store
await new PromptEntry { Name = "my-prompt", Version = "v1", Template = "..." }.Save();

// Retrieve
var entry = await Prompt.Load("my-prompt");
```

## Loading Strategies

| Strategy | Behaviour |
|----------|-----------|
| Latest (default) | Always loads the most recent version |
| `PromptStrategy.Pinned("v3")` | Always loads a specific version |
| `PromptStrategy.AbTest(["v2","v3"])` | Randomly selects between versions |
| `PromptStrategy.Canary("v4", weight: 0.1)` | Routes 10% traffic to canary version |

## Reference

- **Related**: `Koan.AI` (pipeline facade), `Koan.AI.Orchestration` (chain composition), `Koan.AI.Training` (training datasets)
