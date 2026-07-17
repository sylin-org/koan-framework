# GardenCoop Embedded — useful local AI, no services to install

This sample saves five co-op produce listings and finds them by meaning. The query `ripe red tomato` ranks
**Heirloom Tomatoes** first while data, vectors, and embeddings all stay in the application process.

It is the local-first version of Koan's Reference = Intent promise:

| Concern | Referenced provider | Runtime resource |
|---|---|---|
| REST and static UI | `Koan.Web.Extensions` | Kestrel |
| Entity persistence | `Koan.Data.Connector.Sqlite` | `gardencoop.db` |
| vector index | `Koan.Data.Vector.Connector.SqliteVec` | the same SQLite file |
| text embeddings | `Koan.AI.Connector.Onnx` | bundled ONNX model |

The application does not select those providers in business code. `[Embedding]` on `Produce` makes its normal
`Save()` create the vector index; the references, configuration, and startup facts explain how that intent is met.

## Run the meaningful path

From the repository root:

```powershell
dotnet run --project samples/guides/g1c2.GardenCoopEmbedded
```

Open <http://localhost:5092>, or call the exact result directly:

```powershell
Invoke-RestMethod 'http://localhost:5092/api/produce/search?q=ripe%20red%20tomato&k=3'
```

The first start loads the bundled model and saves five starter listings, so it is slower than later starts. No
Docker runtime, network model endpoint, vector server, API key, or manual bootstrap code is required.

Useful inspection points:

- `GET /api/produce` — the conventional API declared by `EntityController<Produce>`;
- `GET /api/produce/search?q=...` — the small custom scored-search intent;
- `GET /.well-known/Koan/facts` — modules, provider decisions, configuration, and composition evidence;
- `GET /health/ready` — canonical readiness.

[`requests.http`](requests.http) contains all four calls.

## Read the application

- [`Program.cs`](Program.cs) is the standard four-line Koan host.
- [`Produce.cs`](Produce.cs) is the business model, embedding intent, and one-line conventional API.
- [`ProduceSearchController.cs`](ProduceSearchController.cs) embeds one query and joins scored vector matches back
  to their Entities in one batch.
- [`Initialization/GardenCoopEmbeddedModule.cs`](Initialization/GardenCoopEmbeddedModule.cs) owns the only earned
  startup responsibility: ensuring the starter business data after AI composition is ready.
- [`appsettings.json`](appsettings.json) supplies the local database and bundled model paths.

## Publish honestly

The supported deployment shape is a **self-contained folder**, not one executable. For Windows x64:

```powershell
dotnet publish samples/guides/g1c2.GardenCoopEmbedded -c Release -r win-x64 `
  -p:PublishSingleFile=true --self-contained true
```

Run `GardenCoopEmbedded.exe` from its publish directory. The executable contains the managed application and .NET
runtime, while native ONNX/SQLite libraries, `models/`, and `appsettings.json` remain beside it. That folder runs
without installing .NET or any external service. This sample does not claim NativeAOT, a literal one-file artifact,
or untested operating-system/RID portability.
