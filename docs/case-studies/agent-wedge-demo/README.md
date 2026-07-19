# The wedge demo — a working multi-provider AI app in one session

> **What this is.** A real, replayable transcript (P5.2) of building `Recs` — an anime catalog that grows
> from a single entity into a cached, job-backed, semantically-searchable, **agent-operable** app — in one
> session on Koan. Every command and every captured output below was run live against the framework source
> on 2026-06-20 (.NET 10, Docker, Ollama). The final app is six files: [`app/`](app/). Nothing is
> fabricated; where a step needed a fix, the fix is shown.
>
> **The headline:** the same `Entity<T>` is, in turn, a table row, a REST resource, a cache entry, a
> background job's output, an embedding source, and an agent tool — and you *add* each capability by
> referencing a package and (sometimes) adding one attribute. The boot report and the checked-in
> `koan.lock.json` narrate the composition the whole way.

---

## Beat 1 — an entity becomes REST

`Program.cs` is the whole bootstrap; `Anime.cs` is the entity + one controller line:

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();          // reflective discovery of every referenced pillar
var app = builder.Build();
app.Run();
```
```csharp
// Anime.cs
public sealed class Anime : Entity<Anime>
{
    public string Title { get; set; } = "";
    public string Synopsis { get; set; } = "";
    public int Episodes { get; set; }
}

[Route("api/anime")]
public sealed class AnimeController : EntityController<Anime> { }   // full REST in one line
```

Build → run → POST → GET (sqlite):

```text
$ curl -X POST :5080/api/anime -d '{"title":"Cowboy Bebop","synopsis":"Bounty hunters in space.","episodes":26}'
{"title":"Cowboy Bebop","synopsis":"Bounty hunters in space.","episodes":26,"id":"019ee7d5-59ff-7a59-9e82-1c88af54348c"}

$ curl :5080/api/anime
[{"title":"Cowboy Bebop","synopsis":"Bounty hunters in space.","episodes":26,"id":"019ee7d5-..."}]
```

The id is a server-assigned **GUID v7**. And the app already explains itself — the boot report (and the
checked-in [`koan.lock.json`](app/) emitted at build, P1.1) name every module it's composed of:

```text
│ Name          : Recs
│ Environment   : Development (Standalone)
│ Runtime       : Koan.Core 0.17.0.0
│ Composition   : 9 modules · lockfile ok        ← P1.1: matches the checked-in koan.lock.json
│ Koan.Data.Connector.Sqlite: 0.17.0.0
│ Koan.Web      : 0.17.0.0
```

## Beat 2 — the composition is something you diff

The build emitted `koan.lock.json` (9 modules). In beat 3 we add one package reference; here's what PR
review would see — `git diff koan.lock.json` is the composition changing:

```diff
   "modules": [
+    { "id": "Koan.Data.Connector.Postgres", "version": "0.17" },
```

## Beat 3 — swap SQLite → Postgres (zero code change)

Add the Postgres connector reference and point the default source at it — **no change to `Anime`**:

```xml
<ProjectReference Include="...Koan.Data.Connector.Postgres.csproj" />
```
```json
{ "Koan": { "Data": { "Sources": { "Default": { "Adapter": "postgres" } } } } }
```

Run Postgres with standard Aspire, Compose, Docker, or another topology owner and provide its standard
`ConnectionStrings:Postgres` value. Koan elects the connector and discovers the supplied endpoint; it does not create
a second container lifecycle:

```text
│ Composition   : 11 modules · lockfile ok
│ Adapters      : 2

# obj/koan.lock.resolved.json (P1.1 resolved twin) — the election, recorded:
"elections": { "data:default": { "adapter": "postgres", "via": "default-source" } }
```

The same REST surface, now on Postgres — and the row is verifiably *in Postgres* (the previous sqlite
data is absent, proving it really swapped):

```text
$ curl -X POST :5081/api/anime -d '{"title":"Steins;Gate","synopsis":"Time travel via microwave.","episodes":24}'
{"title":"Steins;Gate",...,"id":"019ee7d6-d8aa-756a-..."}

$ docker exec <koan-provisioned-pg> psql -d public -tAc 'SELECT "Title","Episodes" FROM "Anime_6838e14b";'
Steins;Gate|24
```

## Beat 4 — transparent caching with one attribute

```csharp
[Cacheable(300)]                       // + reference Koan.Cache
public sealed class Anime : Entity<Anime> { ... }
```
```text
│ Composition   : 13 modules · lockfile ok
info| Koan.Cache topology: local-only (L1=memory, no Remote registered).   ← fresh-or-null L1, writes evict
```

`Todo.Get(id)` is unchanged; reads are now L1-cached and writes evict — no `services.AddCache()`.

## Beat 5 — a background job is just an entity

```csharp
public sealed class ImportAnime : Entity<ImportAnime>, IKoanJob<ImportAnime>   // + reference Koan.Jobs
{
    public string[] Titles { get; set; } = [];
    public static async Task Execute(ImportAnime job, JobContext ctx, CancellationToken ct)
    {
        foreach (var t in job.Titles)
            await new Anime { Title = t, Synopsis = "(imported by job)", Episodes = 12 }.Save();
    }
}
```

Because a data adapter (Postgres) is present, the ledger auto-upgrades to the **durable** tier — no queue
or worker wired:

```text
info| [Koan.Jobs] ledger=RoutingJobLedger · 1 job types · 0 scheduled · claim=Optimistic

$ curl -X POST :5082/api/import -d '["Frieren","Vinland Saga","Monster"]'
{"jobId":"019ee7da-f21a-...","queued":3}

$ curl :5082/api/anime          # a moment later
total anime: 4
 - Steins;Gate
 - Frieren
 - Vinland Saga
 - Monster
```

## Beat 6 — AI as a property of the data

```csharp
[Embedding(Properties = new[] { "Title", "Synopsis" }, Model = "all-minilm", Async = true)]
public sealed class Anime : Entity<Anime> { ...; public float[]? Embedding { get; set; } }
```

References: `Koan.Data.AI`, `Koan.Data.Vector`, the Weaviate connector, the Ollama connector. Two things
happened live worth recording honestly:

- **Port conflict** — Koan tried to self-orchestrate Weaviate on `:8080`, but another service already held
  that port, so it **failed loud** (`KoanBootException`) rather than silently misbehaving. Fix: start a
  Weaviate on a free port and set `Koan:Data:Weaviate:Endpoint` — "user-explicit endpoint beats
  auto-discovery", and it connected.
- **`Async = true`** routes the embed through the worker that writes the vector to the store; with the
  synchronous default the vector lands on the entity but not (yet) in Weaviate.

Then semantic search is just a query (embed the text, ask for neighbours):

```text
$ curl ':5084/api/search?q=undercover family hiding secret identities'
  0.3514  Spy x Family            ← #1, semantically
  0.1452  Cowboy Bebop

$ curl ':5084/api/search?q=epic viking revenge saga'
  0.3517  Vinland Saga            ← #1, semantically
  0.1971  Cowboy Bebop

│ Composition   : 24 modules · lockfile ok
info| Generated embeddings via adapter ollama (Embed) for model all-minilm
```

## Beat 7 — the same entity, now an agent tool

```csharp
[McpEntity(Name = "anime", Description = "Anime catalog entries")]   // + reference Koan.Mcp
[Access(read: "anyone", write: "anyone")]                           // the SAME gate REST enforces
public sealed class Anime : Entity<Anime> { ... }
```
```json
{ "Koan": { "Mcp": { "EnableHttpSseTransport": true } } }
```

Now an **agent** (here, a raw MCP client over the Streamable HTTP transport — AI-0037) initializes a
session, reads the introspection resource `koan://entities`, and mutates through a tool — over JSON-RPC,
no bespoke API:

```text
# initialize → serverInfo names the app, a session id is issued
POST /mcp {"method":"initialize",...}
  Mcp-Session-Id: 8f02c243...
  {"result":{"serverInfo":{"name":"Recs","version":"0.17.0.0"},"capabilities":{"tools":{},"resources":{}}}}

# the agent reads the entity catalog (P1.2) — verbs + which ones mutate
POST /mcp {"method":"resources/read","params":{"uri":"koan://entities"}}
  { "entities": [ { "name": "anime", "verbs": [
      {"name":"anime.query","isMutation":false}, {"name":"anime.get-by-id","isMutation":false},
      {"name":"anime.upsert","isMutation":true}, {"name":"anime.delete","isMutation":true} ] } ] }

# the agent mutates through the tool (schema-validated: {model:{...}})
POST /mcp {"method":"tools/call","params":{"name":"anime.upsert",
           "arguments":{"model":{"Title":"Made in Abyss","Synopsis":"Children descend a deadly abyss.","Episodes":13}}}}
  isError: False  →  {"Title":"Made in Abyss",...,"Id":"019ee7e8-055f-..."}
```

The agent's write is the same entity REST serves — `GET /api/anime` now returns "Made in Abyss". One
entity, two faces, one `[Access]` gate enforced identically on both.

> **Governed access (SEC-0005 / P3.1).** This run used an open `write: "anyone"` gate, so the mutation is
> ungoverned and unaudited. Tighten it to `write: "has:scope:anime:write"` and the same anonymous agent is
> *denied* — the verb becomes a **Door** that discloses the scope it `needs`; an `AgentGrant` materializes
> the access for a specific subject, and each granted mutation writes an `AgentAction` audit row (reads are
> never audited). That governance layer is the shipped P3.1 prerequisite; it is the *same* `[Access]` gate,
> just non-open.

---

## What the demo proves

| Beat | Added by | The entity's new face | Modules |
|---|---|---|---|
| 1–2 | `Koan.Web` + a connector | REST + a diffable `koan.lock.json` | 9 |
| 3 | a Postgres reference + 1 config line | runs on Postgres (self-orchestrated) | 11 |
| 4 | `[Cacheable]` | transparently cached reads | 13 |
| 5 | `IKoanJob<T>` | background work (durable ledger) | 13 |
| 6 | `[Embedding]` + Ollama + Weaviate | semantic search | 24 |
| 7 | `[McpEntity]` | an agent tool over MCP | 27 |

Six files, ~80 lines of app code, zero `services.AddX()` plumbing beyond `AddKoan()` — and the boot report
+ `koan.lock.json` told the truth about the composition at every step.

The final source: [`app/`](app/).
