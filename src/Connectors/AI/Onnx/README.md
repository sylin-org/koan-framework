# Sylin.Koan.AI.Connector.Onnx

In-process ONNX sentence embeddings for Koan AI. It is side-loadable and air-gap friendly: Koan downloads nothing at
runtime.

> **Maturity:** Supported 0.20 extension within the model and tokenizer boundaries below.

## Install

```powershell
dotnet add package Sylin.Koan.AI.Connector.Onnx
```

## Smallest meaningful use

Reference plus configuration is the complete setup:

```json
{
  "Koan": {
    "Ai": {
      "Onnx": {
        "ModelPath": "Models/all-MiniLM-L6-v2/model.onnx",
        "VocabPath": "Models/all-MiniLM-L6-v2/vocab.txt",
        "ModelName": "all-MiniLM-L6-v2"
      }
    }
  }
}
```

```csharp
using Koan.AI;

float[] vector = await Client.Embed("A clean, business-readable application.");
```

When `ModelPath` is absent, the referenced provider remains inactive and startup reports why. Once a path is
configured, it is explicit intent: a missing model or vocabulary fails startup with the corrective path instead of
silently removing embeddings.

The provider publishes one in-process `onnx` source with Embedding capability. The ONNX session is a DI-owned
singleton and is disposed with the host.

## Boundaries

The model must be BERT-compatible with the tokenizer and tensor shapes implemented by this adapter. Koan does not
download, convert, quantize, update, or judge the semantic quality of the model. Missing or incompatible artifacts
fail explicitly.

See [TECHNICAL.md](./TECHNICAL.md) for model assumptions and pooling behavior.
