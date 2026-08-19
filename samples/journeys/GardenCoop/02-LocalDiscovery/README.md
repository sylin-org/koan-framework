# GardenCoop 02 — Local Discovery

Chapter 2 is the complete Garden Journal plus one new business capability: describe produce in ordinary
words and find the closest co-op harvest. The query `ripe red tomato` ranks **Heirloom Tomatoes** first
while data, vectors, and embeddings stay local.

## Run the cumulative result

From the repository root:

```pwsh
dotnet run --project samples/journeys/GardenCoop/02-LocalDiscovery -- --urls http://localhost:5092
```

Open <http://localhost:5092>. The Dashboard and Admin tabs retain Chapter 1's watering workflow; **Local
discovery** adds semantic produce search. No Docker runtime, model endpoint, vector server, API key, or
manual bootstrap is required. First start loads the bundled ONNX model and is slower than later starts.

Useful inspection points:

- `GET /api/garden/reminders` — Chapter 1's watering result;
- `GET /api/garden/produce` — five conventional Produce entities;
- `GET /api/garden/produce/search?q=ripe%20red%20tomato&k=3` — the new meaningful result;
- `GET /.well-known/Koan/facts` — composition and provider decisions;
- `GET /health/ready` — canonical readiness.

## The capability addition

Chapter 2 keeps the same four-line host, garden entities, controllers, automation, UI, and executable
contract. It adds three references—local ONNX embeddings, SQLite vector storage, and Entity embedding
integration—plus the business-aligned `Produce` entity and one scored-search endpoint.

`[Embedding]` on `Produce` makes its normal `Save()` create the local vector index. `GardenCoopModule.Compose`
declares the source-owned `garden-produce` vector shape once—name, 384 dimensions, and embedding model—while
referenced providers supply the mechanics. Application code does not select providers or orchestrate their startup.
The module's startup responsibility remains ensuring five starter produce listings after AI composition is ready.

The supported deployment shape is a self-contained folder. This chapter does not claim NativeAOT, a literal
single-file artifact, or untested operating-system portability.

---

**Working with a coding agent?** [AGENTS.md](../../../../AGENTS.md) at the repository root orients any
agent to Koan's application grammar, the evidence an application produces about itself, and the
capability map. If you lifted this sample out of the repository, start from the
[agent retrieval map](https://github.com/sylin-org/koan-framework/blob/v1.0.0/llms.txt).
