# SnapVault — a local photo studio that grows with you

SnapVault accepts a photo, durably organizes it into a daily event, serves the original and gallery recipes,
and can grant one known durable client access to that event without exposing the rest of the studio.

## Run it

From a new Koan checkout:

```powershell
dotnet run --project samples/applications/SnapVault
```

Open `http://localhost:5086` and upload a JPEG or PNG. No container, database server, AI model, vector service,
cloud credential, schema command, or tenant identifier is required. SQLite records and local blobs are kept
under `samples/applications/SnapVault/.koan/`.

The upload returns immediately with a batch identifier. A durable Koan Job stages and processes each file,
creates or reuses its UTC-day event, stores the photo, and reports progress to the browser through Server-Sent
Events. Refreshing or restarting the application does not turn that work into an application-managed queue.

## Read the business in the code

- [`Program.cs`](Program.cs) is the standard `AddKoan().AsWebApi()` host plus the SPA fallback.
- [`PhotoAsset`](Models/PhotoAsset.cs) declares the stored photo, media source, embedding, relationships, and
  event-scoped access rules.
- [`PhotoProcessingJob`](Models/PhotoProcessingJob.cs) is the durable, studio-carrying unit of ingest work.
- [`PhotoProcessingService`](Services/PhotoProcessingService.cs) owns the photo-specific ingest and enrichment
  decisions.
- [`PhotosController`](Controllers/PhotosController.cs) owns upload and photo-specific HTTP; ordinary Entity reads
  come from `EntityController<PhotoAsset>`.
- [`PhotoRecipes`](Media/PhotoRecipes.cs) names the gallery transformations served on demand.

That is the intended division: application code says photo studio; Koan owns provider election, persistence,
tenant context, durable execution, media plumbing, access filtering, health, and runtime explanation.

## Optional enrichment

The application contains AI-analysis and semantic-search behavior, but the local composition deliberately does
not select an AI or vector backend. An unavailable AI provider is an explicit, retryable enrichment state; it
does not make an otherwise valid photo fail ingest.

To enable enrichment, reference an eligible Koan AI connector and vector connector in `SnapVault.csproj`, then
configure those providers using their connector documentation. If a referenced provider participates at runtime,
Koan reports it as an application dependency and exposes its health and correction. Package presence alone does
not make an unused external service readiness-critical.

## Inspect the running application

Use [`requests.http`](requests.http), or inspect these directly:

- `GET /health/ready` — whether the selected runtime composition can serve work.
- `GET /.well-known/Koan/facts` — elected providers, capabilities, guarantees, and corrections.
- `GET /api/photos/stats` — the current studio totals.
- `GET /media/{photoId}` and `GET /media/{photoId}/gallery` — the original and recipe under the current validated request context.

Startup reporting tells the same story: SQLite and local storage are the default local mechanisms, Jobs owns
durable processing, and optional providers appear only when they actually participate.

## Honest boundaries

The checked-in Development posture supplies a local operator identity so the complete studio can be explored
without external identity infrastructure. It is not a production authentication policy. A deployed application
must choose real authentication and tenant resolution, durable infrastructure appropriate to its availability
requirements, backup and retention policy, and any desired AI/vector providers.

The meaningful supported upload path is JPEG and PNG, at most 10 files per batch and 25 MB per file. The sample
does not claim HEIC decoding, production scale, or certification of every optional provider combination.

## Verify the contract

```powershell
dotnet test tests/Suites/Samples/Koan.Samples.SnapVault.Tests/Koan.Samples.SnapVault.Tests.csproj -c Release
```

The cumulative proof boots the real web application on SQLite, uploads a generated JPEG over HTTP, drains its
durable job, verifies the stored event and photo, serves media, and checks readiness and runtime facts. Focused
tests also cover tenant isolation, guest gallery grants, proofing, cleanup, progress, and mutation behavior.
