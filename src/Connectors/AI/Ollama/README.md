# Sylin.Koan.AI.Connector.Ollama

Ollama AI provider for Koan: local LLM chat, stream, and embeddings via Ollama endpoint.

- Target framework: net9.0
- License: Apache-2.0

## Install

```powershell
dotnet add package Sylin.Koan.AI.Connector.Ollama
```

## Minimal setup

Register Koan + the Ollama provider (typical ASP.NET Program.cs):

```csharp
// using Koan.AI; using Koan.AI.Connector.Ollama; using Koan.AI.Web;
var builder = WebApplication.CreateBuilder(args);
builder.Services
		.AddKoan()
		.AddAi()
		.AddOllama(o =>
		{
				o.BaseAddress = new Uri("http://localhost:11434"); // default
				o.DefaultModel = "qwen3:4b"; // or any local model/tag
		})
		.AddAiWeb(); // optional HTTP endpoints under /ai
var app = builder.Build();
app.MapControllers();
app.Run();
```

Then query the default engine:

```csharp
using Koan.AI;
var res = await Engine.Prompt("Explain quantum entanglement briefly.");
Console.WriteLine(res.Text);
```

Or via HTTP (if AddAiWeb is enabled):

POST /ai/chat
{
	"model": "qwen3:4b",
	"messages": [{ "role": "user", "content": "Explain quantum entanglement briefly." }]
}

## Using reasoning (think)

Set the optional think flag in prompt options. The Ollama adapter emits a top-level `think` in its JSON payload for models that support it (e.g., Qwen3/R1/DeepSeek‑V3.1 quants).

```csharp
using Koan.AI;
using Koan.AI.Contracts.Models;

var res = await Engine.Prompt(
		"Briefly explain quantum entanglement.",
		model: "qwen3:4b",
		opts: new AiPromptOptions { Think = true }
);
Console.WriteLine(res.Text);
```

HTTP example:

POST /ai/chat
{
	"model": "qwen3:4b",
	"messages": [{ "role": "user", "content": "Explain quantum entanglement briefly." }],
	"options": { "think": true }
}

## Vendor-specific options (passthrough)

Unknown fields posted under `options` are forwarded to Ollama’s `options` bag in the request. Use this for provider-specific parameters like `mirostat`, `repeat_penalty`, etc. Known Koan fields still map to Ollama (`temperature`, `top_p`, `num_predict`, `stop`). If keys overlap, your posted value wins.

HTTP example:

POST /ai/chat
{
	"model": "qwen3:4b",
	"messages": [{ "role": "user", "content": "Summarize Bell’s theorem in one sentence." }],
	"options": {
		"temperature": 0.6,
		"topP": 0.95,
		"maxOutputTokens": 128,
		"mirostat": 2,
		"mirostat_tau": 5.0,
		"repeat_penalty": 1.1
	}
}

Programmatic example:

```csharp
using Koan.AI;
using Koan.AI.Contracts.Models;

var opts = new AiPromptOptions
{
		Temperature = 0.6,
		TopP = 0.95,
		MaxOutputTokens = 128,
		// Add arbitrary vendor params via object initializer syntax in JSON calls
};
var res = await Engine.Prompt("Summarize Bell’s theorem in one sentence.", "qwen3:4b", opts);
```

Notes
- The Web API uses `messages[]` instead of a top-level `prompt`.
- Embeddings are supported via POST /ai/embeddings with `model` and `input` (array of strings).
- Streaming is supported via POST /ai/chat/stream with the same body as /ai/chat.

## Links
- Ollama: https://ollama.com

