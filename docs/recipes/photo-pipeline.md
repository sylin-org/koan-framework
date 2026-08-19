---
type: RECIPE
recipe: photo-pipeline
title: "Let people upload photos and find them by what's in them"
domain: media
status: current
last_updated: 2026-08-19
audience: [ai-agents, developers]
framework_version: v1.0.0
validation:
  status: source-verified
  scope: snippets copied from samples/applications/SnapVault, which compiles and runs
gets_you: "Upload once; get web and thumbnail versions, a description of the contents, and search by describing what you remember."
works_if: "Users send you images and will later want to find them without having tagged anything."
costs: "Disk for originals, CPU for derivatives, and a local vision model that is large — plan for RAM and disk."
ingredients:
  - "one | Entity-owned files | Sylin.Koan.Storage, Sylin.Koan.Storage.Connector.Local"
  - "one | named derivative recipes over HTTP | Sylin.Koan.Media.Core, Sylin.Koan.Media.Web"
  - "one | durable ingest that survives restarts | Sylin.Koan.Jobs"
  - "one | vision and embedding runtime | Sylin.Koan.AI, Sylin.Koan.AI.Connector.Ollama"
  - "one | embedding ownership and vector search | Sylin.Koan.Data.AI, Sylin.Koan.Data.Vector, Sylin.Koan.Data.Vector.Connector.SqliteVec"
  - "optional | live upload progress | Sylin.Koan.Web.Sse"
---

# Let people upload photos and find them by what's in them

This is the compound that [SnapVault](https://github.com/sylin-org/koan-framework/blob/v1.0.0/samples/applications/SnapVault/README.md)
runs: one upload becomes a stored original, several derivatives, a described set of contents, and a
searchable vector. Everything here is taken from that application, which compiles.

## The shape that makes it work

**Stage the upload, then process it in a job.** The request stores raw bytes and returns; a durable job
does storage, metadata extraction, analysis, and embedding. Delete the staged blob **only after
success**, so a retry can reread the original bytes. This is the detail that separates a pipeline that
survives a restart from one that loses uploads.

**Derive on request, not on ingest.** Store one original and let named recipes render sizes on demand.
You avoid regenerating everything when the design changes, and you never overwrite what the user sent.

**The chat model and the embedding model are different.** Vision runs through the chat category;
embeddings need their own. Inheriting one for the other fails at the provider.

## Assembly

The Entity owns its media and declares where the bytes live and what represents it:

```csharp
[StorageBinding(Profile = "cold", Container = "photos")]
[Embedding(
    Policy = EmbeddingPolicy.AllStrings,
    Async = true,
    Model = "nomic-embed-text",
    Version = 2,
    Exclude = ["EventId", "InferredStyleId"])]
public class PhotoAsset : MediaEntity<PhotoAsset>
{
    public string OriginalFileName { get; set; } = "";
    public List<string> AutoTags { get; set; } = new();
    public string MoodDescription { get; set; } = "";
    public float[]? Embedding { get; set; }   // Koan recognizes float[] as the search vector
}
```

`MediaEntity<T>` rather than `Entity<T>` is what makes it media-owning. `Async = true` moves embedding
off the save. `Version` matters: stored and query embeddings must share one vector space, so bumping
the model means bumping the version and re-embedding.

Derivatives are declared once and discovered automatically:

```csharp
public static class PhotoRecipes
{
    [MediaRecipe("gallery", Description = "1200px web view, JPEG")]
    public static MediaRecipe Gallery() => MediaRecipe.New().ResizeFit(1200, 1200).EncodeAs("jpeg");

    [MediaRecipe("masonry", Description = "300px masonry grid tile, JPEG")]
    public static MediaRecipe Masonry() => MediaRecipe.New().ResizeFit(300, 300).EncodeAs("jpeg");
}
```

`Koan.Media.Web` serves them at `GET /media/{id}/{name}`; the seedless route returns the original.
Recipe names are global slugs and must avoid the reserved format shortcuts (`jpeg`, `png`, `webp`,
`gif`). The engine auto-orients by default, so no orient step is needed.

Ingest is a job with named actions, reporting progress into the Jobs ledger:

```csharp
[JobAction(Ingest, Timeout = "00:15:00", MaxAttempts = 3)]
[JobAction(Reanalyze, Timeout = "00:10:00", MaxAttempts = 3)]
public sealed class PhotoProcessingJob : Entity<PhotoProcessingJob>, IKoanJob<PhotoProcessingJob>
{
    public static async Task Execute(PhotoProcessingJob job, JobContext ctx, CancellationToken ct)
    {
        switch (ctx.Action)
        {
            case Ingest:
                await using (var raw = await UploadStaging.OpenRead(job.StagingKey, ct))
                    await service.ProcessUpload(raw, (f, stage) => ctx.Progress(f, stage), ct);
                await UploadStaging.Get(job.StagingKey).Delete(ct);   // only after success
                break;
        }
    }
}
```

## Prove it

1. **Behavior** — upload an image, request a named derivative, and assert semantic search finds it from
   a description of its contents rather than its filename.
2. **Composition** — assert the storage profile, vision model, embedding model, and vector store that
   actually participated. Five pieces cooperate here and any one can be silently wrong.
3. **Correction** — kill the host mid-ingest and assert the job resumes from the staged blob; point the
   embed category at a vision model and assert it fails loudly rather than storing a bad vector.

## Boundaries

- Nothing here provisions a model, backs up your disk, or scans uploads.
- Analysis is a suggestion. If it drives anything a customer sees, add
  [review before it ships](review-ai-output.md).
- Changing the embedding model invalidates the index; that is why `Version` exists.

## Interacts with

**Tenancy.** SnapVault is multi-tenant, and the reason it works is that Koan captures the ambient
tenant at job submission and restores it before execution — so Data, Storage, and Vector all stay
inside the right tenant with no routing code in the job. Without that carriage, background ingest reads
nothing and appears to succeed.
