# S10.DevPortal — publish through named provider channels

S10 is a business-aligned provider-composition sample. Editors draft articles in a local SQLite store,
approve the ones ready for readers, and publish the approved set to a named channel without changing the
`Article` model or its Entity grammar.

## Shortest meaningful path

From the repository root:

```powershell
Set-Location samples/S10.DevPortal
dotnet run
```

Open <http://localhost:5090>, then choose **Reset the editorial story** and **Publish to preview**.
The result is three editorial articles, two approved preview articles, and one draft that remains local.
Publishing to preview again upserts the same two identities; it does not create duplicates or remove the
editorial copies.

No container or external service is required. The two local sources are separate SQLite files under
`.koan/`:

- `Default` is the editorial store;
- `Preview` is the local publication channel.

## The application language

The host is the standard four-line Koan application:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

The provider-independent business action stays on the Entity:

```csharp
await Article.Copy(article => article.Status == ArticleStatus.Approved)
    .To(source: PublicationChannel.Preview.ToString())
    .Run(ct);
```

`ArticleController : EntityController<Article>` exposes the normal editorial CRUD API. The custom
`PublicationController` owns only the reset, publish, and channel-read workflow.

## Optional external channels

S10 also references Mongo and Postgres connectors. Named source configuration maps business channels to
their mechanics:

| Channel | Provider | Requirement |
|---|---|---|
| `Preview` | SQLite | none beyond a writable sample directory |
| `Documents` | Mongo | Mongo reachable at `localhost:5091` |
| `Relational` | Postgres | Postgres reachable at `localhost:5092` |

Start either or both sample services with standard Docker Compose:

```powershell
docker compose -f docker/compose.yml up -d mongo postgres
```

Then publish from the dashboard or with the requests in `requests.http`. Selecting an unavailable external
channel returns `503` with a corrective instruction. Koan does not silently fall back to SQLite or report a
global provider switch.

Stop the optional services when finished:

```powershell
docker compose -f docker/compose.yml down
```

## What this sample proves

- direct connector references make SQLite, Mongo, and Postgres available;
- the configured `Default` source deliberately selects SQLite instead of relying on connector priority;
- the same `Article` Entity and transfer expression work through named source routing;
- publication is an idempotent copy of a deliberately small approved set;
- startup and `/.well-known/Koan/facts` explain the composition and selected default;
- unused external connectors do not become a hidden prerequisite for the local path.

The current transfer builder materializes its selected source before destination batches. This sample does
not claim streaming, checkpoints, cross-provider transactions, benchmark equivalence, or automatic data
migration. Large/resumable publication flows need an application-owned bounded stream and job/checkpoint policy.
