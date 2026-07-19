# Anime Recommendations

Rate anime you know, describe what you feel like watching, and receive explainable recommendations shaped by both.
The entire journey runs locally: SQLite stores the catalog and ratings, ONNX embeds meaning, and sqlite-vec finds the
nearest stories.

```powershell
dotnet run --project samples/applications/AnimeRecommendations
```

Open the URL printed by .NET (the launch profile uses `http://localhost:5094`). A clean first run curates 24 anime,
indexes their meaning, and opens Mika's profile with three ratings ready. No container, model server, vector server,
account, or network import is required.

## The application

The business is deliberately smaller than the retired Recs implementation:

- `Anime : Entity<Anime>` declares `[Embedding]`; its normal `Save()` persists and indexes the story.
- `Viewer : Entity<Viewer>` names whose taste is being served.
- `LibraryEntry : Entity<LibraryEntry>` records one current 1–5 rating for one viewer/anime pair.
- `AnimeDiscovery.Recommend(...)` is the one irreducible multi-Entity workflow. It derives intent from a bounded
  rating ledger plus the current mood, asks `Vector<Anime>.Search` for candidates, removes titles already rated, and
  explains the strongest matching signals.
- Entity controllers own ordinary catalog/viewer/library HTTP. Two thin MVC controllers own the business actions
  “rate this” and “recommend for me.”

`Program.cs` stays the complete four-line host. Direct project/package references express the SQLite, Web, OpenAPI,
Data.AI, ONNX, and sqlite-vec capability intent; `AddKoan()` discovers and composes them.

## Try the API

```http
GET /api/anime/catalog
PUT /api/anime/viewers/demo/ratings/pluto
Content-Type: application/json

{ "rating": 5 }

GET /api/anime/recommendations?viewerId=demo&mood=thoughtful%20science%20fiction&take=8
```

Use `/swagger` in Development for the interactive API, `/health/ready` for readiness, and
`/.well-known/Koan/facts` to see the elected data, AI, vector, and Web providers. `requests.http` contains the same
journey in a copyable form.

## What this proves—and what it does not

This is a coherent local content-recommendation application, not a claim of internet-scale recommendation quality.
The starter catalog and rating history are deliberately bounded, so request-time taste derivation and in-memory
explanation are honest. The sample does not claim remote catalog import, collaborative filtering, model training,
authentication, production authorization, distributed execution, or an operations control plane.

To replay first-run curation, stop the host and remove this sample's `.koan` directory. The application creates no
state outside that directory during an ordinary run.
