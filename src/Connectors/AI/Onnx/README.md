# Sylin.Koan.AI.Connector.Onnx

In-process ONNX sentence embeddings for Koan AI. It is side-loadable and air-gap friendly: Koan downloads nothing at
runtime.

The generated [product surface](../../../../docs/reference/product-surface.md) owns support maturity;
this page owns ONNX setup and limits.

## Install

```powershell
dotnet add package Sylin.Koan.AI.Connector.Onnx
```

## Get the model artifacts

The package ships no model - that is what makes it air-gap friendly. Two BERT-compatible files
are required: the ONNX graph and its wordpiece vocabulary. The reference set (all-MiniLM-L6-v2,
quantized, 384 dimensions) is served from the Koan repository and downloads once into your
project:

```powershell
# from your project root
New-Item -ItemType Directory -Force models/all-MiniLM-L6-v2 | Out-Null
Invoke-WebRequest https://raw.githubusercontent.com/sylin-org/koan-framework/main/assets/models/all-MiniLM-L6-v2/model_quantized.onnx -OutFile models/all-MiniLM-L6-v2/model_quantized.onnx
Invoke-WebRequest https://raw.githubusercontent.com/sylin-org/koan-framework/main/assets/models/all-MiniLM-L6-v2/vocab.txt -OutFile models/all-MiniLM-L6-v2/vocab.txt
```

For air-gapped machines, fetch the two files on a connected machine and copy the folder across.
Relative artifact paths resolve against the build output, so let the SDK carry them:

```xml
<Content Include="models\**" CopyToOutputDirectory="PreserveNewest" />
```

## Smallest meaningful use

Reference, artifacts, plus configuration is the complete setup (the package distributes no model
files - see [Get the model artifacts](#get-the-model-artifacts)):

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
singleton and is disposed with the host. Runtime health is inspected by that loaded adapter: `inproc://onnx` is an
identity for routing and diagnostics, not an HTTP endpoint, so no network probe is made.

## Boundaries

The model must be BERT-compatible with the tokenizer and tensor shapes implemented by this adapter. Koan does not
download, convert, quantize, update, or judge the semantic quality of the model. Missing or incompatible artifacts
fail explicitly.

See [TECHNICAL.md](./TECHNICAL.md) for model assumptions and pooling behavior.
