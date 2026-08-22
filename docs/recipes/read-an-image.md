---
type: RECIPE
recipe: read-an-image
title: "Read or describe an image"
domain: ai
status: current
last_updated: 2026-08-19
audience: [ai-agents, developers]
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/recipes/read-an-image.md
gets_you: "Text out of a picture — the words on a receipt, or a description of what is in a photo."
works_if: "The application already has images, or a way for users to send them."
costs: "Runs offline with a local multimodal model, which is larger than a text model — plan for RAM and disk."
ingredients:
  - "one | AI runtime | Sylin.Koan.AI"
  - "one-or-more | multimodal model runtime, user's choice | Sylin.Koan.AI.Connector.Ollama, Sylin.Koan.AI.Connector.LMStudio, Sylin.Koan.AI.Connector.HuggingFace"
absent:
  - "hosted vision model | no OpenAI, Anthropic, or Gemini connector exists | run a local multimodal model, or call the vendor directly with an HttpClient"
---

# Read or describe an image

Koan routes this through the `Ocr` category, which delegates to a multimodal chat model rather than
requiring a runtime of its own.

## When this is the answer

"What's on this receipt", "tag these photos automatically", "is there a person in this image". If the
developer instead wants to *store* and *resize* images, that is storage and media rather than AI —
different recipe, and note that media needs a storage connector for the bytes.

**The model must be multimodal.** `Ocr` defaults to `Via: "Chat"`, so the model behind the chat
category has to accept images, or `Koan:Ai:Ocr` must name one that does. A text-only model configured
here fails at the provider rather than degrading.

Multimodal models are materially larger than text models. If the developer is running locally on a
laptop, say that before they pull one.

## Assembly

```powershell
dotnet add package Sylin.Koan.AI
dotnet add package Sylin.Koan.AI.Connector.Ollama
```

Point the categories at models that can do the job:

```json
{ "Koan": { "Ai": {
  "Chat": { "Model": "qwen2.5vl" },
  "Ocr":  { "Via": "Chat" }
} } }
```

`Ocr` accepts `Source`, `Model`, `Via`, and `Fallback` independently — set `Model` here when the OCR
model should differ from the general chat model.

## Prove it

1. **Behavior** — a fixture image with known content, asserted on the extracted text or on a
   substantive claim about the description. Never assert exact model prose.
2. **Composition** — assert the intended provider and model were selected.
3. **Correction** — configure a text-only model and assert the failure is explicit rather than an
   empty or invented description.

## Boundaries

- Referencing the packages does not acquire the model.
- Nothing here stores the image, tracks derivatives, or governs who may view it. Those are storage,
  media, and authorization concerns at their own boundaries.
- Extraction quality varies sharply by model and image quality; treat output as a suggestion unless a
  human or a downstream check confirms it.

## Interacts with

**Media and storage.** If images belong to Entities, the bytes live behind a storage connector and the
derivative pipeline is separate. Adding vision does not give an application anywhere to put a file.
