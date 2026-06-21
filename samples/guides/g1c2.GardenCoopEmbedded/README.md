# GardenCoop, embedded — the single-binary slice

A near-copy of [`g1c1.GardenCoop`](../g1c1.GardenCoop/), but every capability is satisfied by an **in-process
resource** instead of a server. No container runtime, no model server, no vector server, no message broker —
the whole stack lives inside one process and (with a self-contained publish) one `.exe`.

This is the concrete proof of the *footprint floor* from the in-process adapter survey
([`docs/assessment/evidence/inproc-adapter-survey.md`](../../../docs/assessment/evidence/inproc-adapter-survey.md)):
"single binary" does **not** mean fewer capabilities — it means every capability runs in-process.

## The embedded stack (Reference = Intent)

`Program.cs` is one line — `builder.Services.AddKoan()`. Which providers light up is decided purely by which
packages the `.csproj` references:

| Capability | In-process resource | Package |
|---|---|---|
| Data (rows) | SQLite (file-backed) | `Koan.Data.Connector.Sqlite` |
| Vector (k-NN) | sqlite-vec (`vec0`, embedded native) | `Koan.Data.Vector.Connector.SqliteVec` |
| Embeddings (text→vector) | ONNX Runtime + all-MiniLM-L6-v2 | `Koan.AI.Connector.Onnx` |
| Messaging | `System.Threading.Channels` bus | `Koan.Messaging.Connector.InMemory` |
| Web / REST | Kestrel (in-process) | `Koan.Web` |

Rows **and** vectors share **one `.db` file** (`gardencoop.db`) — see `appsettings.json`, where both the SQLite
data adapter and sqlite-vec point at the same `Data Source`. The embedding model travels with the app as
content under `models/` (the `.csproj` copies it from `assets/models/all-MiniLM-L6-v2`).

## Run it

```bash
dotnet run
```

On first run it seeds five produce listings. For each, `Seed.cs` embeds the description **in-process** with the
local ONNX model via the AI facade (`Client.Embed`) and writes the vector to sqlite-vec (`Vector<Produce>.Save`). Then:

```bash
# REST (auto from EntityController<Produce>)
curl http://localhost:5099/api/produce

# Semantic search — the query is embedded by the local model and matched in sqlite-vec
curl "http://localhost:5099/api/produce/search?q=ripe%20red%20tomato"
# => Heirloom Tomatoes ranks first, with no server in sight
```

## One `.exe`, no container

Self-contained single-file publish bundles the runtime, the native `vec0` + ONNX binaries, and the model into
one executable:

```bash
dotnet publish -c Release -r win-x64 \
  -p:PublishSingleFile=true --self-contained true
# -> bin/Release/net10.0/win-x64/publish/GardenCoopEmbedded.exe  (runs offline, no install)
```

Swap `-r linux-x64` / `-r linux-arm64` for the edge/appliance targets — the vec0 native ships for all three
floor RIDs (embedded in the connector and self-extracted at load).

### NativeAOT (stretch)

NativeAOT (`-p:PublishAot=true`) is the *aspirational* form of this rung. The managed pieces (SQLite + brute-force
vector + Channels) are AOT-clean; the native dependencies here (ONNX Runtime, sqlite-vec) are **AOT-unverified**
per the survey. `NativeAotRoots.xml` seeds the trim roots; treat a successful AOT publish as the `S2.Sovereign-proof`
spike, not a guarantee. Self-contained single-file is the honest single-binary deliverable today.

## Notes

- The embed→store flow uses the **AI facade** (`Client.Embed` → `Vector<Produce>.Save`): the in-process ONNX
  connector publishes itself as an AI *source* (provider `onnx`, the embedded all-MiniLM model as its default),
  so it's a first-class citizen of the same source/router pipeline that drives Ollama — `Client.Embed` and the
  `[Embedding]` worker route to it with no provider name in app code. The adapter mirrors the Ollama shape:
  provider-level identity, model is a usage-time concern (here, the one model bundled in the exe).
- The fully-declarative `[Embedding(Async = true)]` auto-flow (no explicit `Client.Embed` call at all) routes
  through a worker whose generic `EmbeddingState<T>` tracker still needs a relational-safe storage name — once
  that lands, `Seed.cs`'s explicit embed call collapses into a plain `item.Save()`.
- Nothing here references a provider by name except the deliberate `[VectorAdapter("sqlitevec")]` routing hint;
  remove it and the in-memory vector floor (`Koan.Data.Vector.Connector.InMemory`) would serve instead.
