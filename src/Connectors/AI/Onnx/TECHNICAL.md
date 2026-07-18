# Sylin.Koan.AI.Connector.Onnx — technical contract

## Responsibility

This package owns WordPiece tokenization, ONNX Runtime inference, attention-mask mean pooling, and optional L2
normalization for an in-process embedding model. It contributes provider id `onnx` to the compiled AI provider plan.

## Activation

- Options bind from `Koan:Ai:Onnx`.
- No `ModelPath` means intentional inactivity.
- A configured path must resolve to an existing ONNX model.
- `VocabPath` defaults to `vocab.txt` beside the model and must exist.
- The DI container owns and disposes the adapter and its `InferenceSession`.
- A successful activation publishes one healthy `inproc://onnx` member with Embedding capability.

Relative paths resolve from `AppContext.BaseDirectory`. `ModelName` is the routing/reporting name;
`Dimension` is a fallback when output metadata cannot determine vector width. `MaxTokens`, `LowercaseInput`, and
`NormalizeEmbeddings` control preprocessing and output.

## Boundaries

The connector expects a BERT-compatible sentence-embedding model and WordPiece vocabulary with the input/output
shapes implemented by the adapter. It does not download, convert, quantize, update, or validate the semantic quality
of a model. A structurally incompatible model fails during startup or inference rather than being silently adapted.
