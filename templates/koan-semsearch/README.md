# KoanSemSearchApp

Semantic search over your own Entities: an `[Embedding]` attribute makes every save produce a
vector, SqliteVec stores it durably, and a ranked search endpoint compares meanings — not keywords.

## Prerequisite

A local Ollama with an embedding model (the app is configured for `nomic-embed-text`):

```powershell
ollama pull nomic-embed-text
```

## Run

```powershell
dotnet run
```

Save two articles about different subjects:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/articles -ContentType application/json -Body '{"title":"Sourdough starter feeding schedule","body":"Keep your sourdough starter alive with daily feedings of equal parts flour and water."}'
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/articles -ContentType application/json -Body '{"title":"Changing a flat tire on the highway","body":"Pull onto the shoulder, engage hazard lights, loosen lugs before jacking, torque in a star pattern."}'
```

Search by meaning — no keyword in common:

```powershell
Invoke-RestMethod 'http://localhost:5000/api/articles/search?q=how%20to%20replace%20a%20punctured%20tire&k=2'
Invoke-RestMethod 'http://localhost:5000/api/articles/search?q=feeding%20my%20bread%20starter&k=2'
```

Each query ranks its subject first.

## Read the application

| File | Business meaning |
|---|---|
| `Article.cs` | the state the application owns; `[Embedding]` decides what "similar" means |
| `ArticlesController.cs` | ordinary Entity CRUD — saves trigger embedding automatically |
| `ArticleSearchController.cs` | embed the query, rank the vector space, load matching Entities |
| `appsettings.json` | which Ollama endpoint and which embedding model to use |
| `Program.cs` | compose referenced Koan capabilities |
| `KoanSemSearchApp.csproj` | the references that make all of the above available |

Change the embedding model and you must re-index: stored and query vectors only compare when the
same model produced them. See [search by meaning](https://github.com/sylin-org/koan-framework/blob/main/docs/capabilities/ai/semantic-search.md)
for the full contract.
