# Sylin.Koan.AI.Connector.HuggingFace

Add Hugging Face Hub model discovery and download to Koan's model lifecycle.

```bash
dotnet add package Sylin.Koan.AI.Connector.HuggingFace
```

## Meaningful use

Reference the connector with `Sylin.Koan.AI.Models`, retain `AddKoan()`, and use the standard model facade:

```csharp
var matches = await Model.Search("BAAI bge", source: "huggingface", ct: cancellationToken);
var model = await Model.Pull(
    "BAAI/bge-small-en-v1.5",
    format: ModelFormat.ONNX,
    ct: cancellationToken);
```

Public models work anonymously. For private or gated models, set `HF_TOKEN` or `Koan:Ai:HuggingFace:Token`.
`CacheDirectory` defaults to `.Koan/models`; `BaseUrl` defaults to `https://huggingface.co` and may target a compatible
self-hosted Hub.

## Guarantees and limitations

- Reference plus `AddKoan()` contributes one DI-owned `huggingface` adapter with `ModelList` and `Pull` capabilities.
- The connector searches Hub metadata, inspects repositories/files, selects a requested or preferred model file,
  downloads it to the configured cache, and reports progress through `Model.Pull`.
- Authentication, HTTP, rate-limit, gated-license, missing-file, invalid model ID, and filesystem failures remain
  visible. Tokens are not published in provenance.
- This is a Hub/model-management connector, not a chat, embedding, OCR, training, conversion, or inference runtime.
  Downloading a model does not make it executable; reference a compatible runtime/adapter separately.

See [TECHNICAL.md](TECHNICAL.md) for activation, selection, download, and security boundaries.
