# test03 — relationships, canonical units, aggregates, and the pantry match

The task that exercises the **relational pillar**: a recipe system with ingredients as first-class
entities (quantities in human units, normalized against a pinned conversion table), declared
recipe→ingredient relationships, aggregate/reference queries ("how many recipes use milk?", "how
many use more than 10 ingredients?"), semantic search, and the pantry match — *"I have milk, salt,
some onions: what can I make?"*

## The contract (v3)

Full task text in `task-rel.txt` (byte-identical across arms; arm lines per `arm-*.txt`). Pinned:

- **Entities:** `recipes` and `ingredients`, both first-class with their own endpoints
  (`GET/POST /api/ingredients`; ingredient id = name). Join lines carry `quantity` + `unit`;
  `salt` is seeded with `null` quantity = presence-only.
- **Conversion table (pinned in the task):** `glass = 240 ml`, `tbsp = 15 ml`, `piece = 1`.
  Quantities compare in canonical units — "2 glasses of milk" must satisfy
  `minQuantity=300ml`.
- **Endpoints:**
  - `GET/POST/PUT/DELETE /api/recipes`, `GET /api/ingredients/{name}/usage-count` → `{"count":N}`
  - `GET /api/recipes/using?ingredient={name}&minQuantity={q}&unit={u}` → JSON array of recipes ≥ q
  - `GET /api/stats` → `{ "recipesWithMoreThan10Ingredients": N, ... }`
  - `POST /api/recipes/match` `{ pantry: [{name, quantity?, unit}] }` → JSON array ordered
    best-first, every element exactly `{title, missingCount, missing[]}` (fully-covered first,
    then by fewest missing)
  - `GET /api/recipes/search?q=` — semantic, local embeddings, keyword-disjoint probes
- Host on **5099**, SQLite persistence, offline, restart-surviving.

## Contract history — why v3 exists

- **v1** — ingredients first-class, but response shapes unpinned. Both arms built working
  relationship queries (usage-count=3, conversion filter ✓, stat ✓) yet failed the
  create-with-embedded-lines and pantry-match shape checks: contract/grader ambiguity, not model
  failure. Koan 9/12, plain 8/12. Not counted as a model verdict.
- **v2** — shapes pinned (the earned pins above), but the ingredient endpoints were dropped:
  *"ingredients do not need their own management endpoints; referencing by name auto-creates
  them."* Textually arm-neutral, effect-neutral it was not: the koan arm stopped modeling
  Ingredient as a first-class entity, so there was no `[Parent]` edge to declare and no
  relationship query to make — the app shipped a hollow relational layer behind a green CRUD
  facade (7/12: usage-count 0, conversion wrong, pantry trivially satisfied). The plain arm had no
  relationship surface to under-use and built the join correctly (12/12).
- **v3 (current)** — v1's first-class ingredient structure + v2's earned shape pins. The contract
  states HTTP observables only (endpoints and pinned shapes); it never names a modeling approach,
  so it stays arm-neutral while restoring the structure that leads either arm to model the
  recipe→ingredient relationship as a real join. The re-run answers the campaign question: was
  the v2 hollow layer contract-steering (v3 fixes it) or a skill gap (v3 does not, and skill v7
  carries the relationship compound).

## Seed corpus + truth table (grader-side; the agent is told only the conversion table and semantics)

Grader seeds 12 ingredients via `POST /api/ingredients` (`{"id":name,"name":name}`, 2xx/409/400
accepted as idempotent), then 6 recipes with join lines:

| Recipe | Ingredients (canonical) |
|---|---|
| Pancakes | milk 480 ml (2 glass), flour, egg |
| Cream Sauce | milk 300 ml (1.25 glass), butter, pasta |
| Big Feast | **12 ingredients** (milk 15 ml/1 tbsp, onions, salt, garlic, tomato, pasta, flour, egg, butter, sugar, chicken, cream) |
| Onion Soup | onions, salt (null), garlic |
| Salted Pasta | pasta, salt (null) |
| Veggie Mix | onions, tomato, garlic |

- `GET /api/ingredients` lists the seeded set (milk, salt present)
- `usage-count(milk)` = **3**
- `recipesWithMoreThan10Ingredients` = **1** (Big Feast)
- `using milk ≥ 300 ml` = **Pancakes + Cream Sauce**, not Big Feast (15 ml)
- Pantry `{milk 480 ml, salt, onions 2 piece, pasta 1 piece}` → Salted Pasta **full match**
  (missing 0), Big Feast missing **8** and **ranked last**
- Semantic probes: "comforting breakfast stack" → Pancakes; "warming winter bowl" → Onion Soup
  (both keyword-disjoint against the whole corpus)

## Checks (14)

build, start, seed-ingredients, list-ingredients, create-recipes (6× 2xx), usage-count,
conversion hit+miss, stat, pantry rank (first = full match, last = Big Feast), pantry
missing-list shape, restart persistence, two semantic probes.

## Verified seams (koan-side) + the docs gap

- `[Parent]` attribute exists (`Koan.Data.Core.Relationships`) with the relationship query
  subsystem (loader/executor/policy) behind the governed GET (`With=` expansion).
- `BeforeSave`-class hooks exist in the endpoint pipeline — canonical-unit normalization is a
  supported seam.
- **Docs gap (v7 candidate):** the relationship surface is documented only in *archived proposals*
  and the MCP projection comment; the skill's one-block (v6) does not mention `[Parent]`. The
  koan arm's discovery of this surface is itself a measured question in this column — v3 separates
  that question from the contract-steering question.

## Fairness / execution

Task body is byte-identical across arms; only the arm line differs. Port 5099; grading lock
respected; arms sequential. **Execution requires Ollama up** (semantic stage embeds through the
app) — do not run while the GPU is user-reserved; confirm with the operator before firing.
Runner: `run-test03.sh` (single stage, 45-min cap, sequential pair).
