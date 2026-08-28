# test03 — relationships, canonical units, aggregates, and the pantry match

The task that exercises the **relational pillar**: a recipe system with ingredients as first-class
entities (quantities in human units, normalized against a pinned conversion table), declared
recipe→ingredient relationships, aggregate/reference queries ("how many recipes use milk?", "how
many use more than 10 ingredients?"), semantic search, and the pantry match — *"I have milk, salt,
some onions: what can I make?"*

## The contract

Full task text in `task-rel.txt` (byte-identical across arms; arm lines per `arm-*.txt`). Pinned:

- **Entities:** `recipes` and `ingredients` (ingredient id = name). Join lines carry
  `quantity` + `unit`; `salt` is seeded with `null` quantity = presence-only.
- **Conversion table (pinned in the task):** `glass = 240 ml`, `tbsp = 15 ml`, `piece = 1`.
  Quantities compare in canonical units — "2 glasses of milk" must satisfy
  `minQuantity=300ml`.
- **Endpoints:**
  - `GET/POST/PUT/DELETE /api/recipes`, `GET /api/ingredients/{name}/usage-count` → `{"count":N}`
  - `GET /api/recipes/using?ingredient={name}&minQuantity={q}&unit={u}` → recipes using ≥ q
  - `GET /api/stats` → `{ "recipesWithMoreThan10Ingredients": N, ... }`
  - `POST /api/recipes/match` `{ pantry: [{name, quantity?, unit}] }` → ranked
    `{ recipe, missingCount, missing[] }` (fully-covered first, then by missing count)
  - `GET /api/recipes/search?q=` — semantic, local embeddings, keyword-disjoint probes
- Host on **5097**, SQLite persistence, offline, restart-surviving.

## Seed corpus + truth table (grader-side; the agent is told only the conversion table and semantics)

| Recipe | Ingredients (canonical) |
|---|---|
| Pancakes | milk 480 ml (2 glass), flour, egg |
| Cream Sauce | milk 300 ml (1.25 glass), butter, pasta |
| Big Feast | **12 ingredients** (milk 15 ml/1 tbsp, onions, salt, garlic, tomato, pasta, flour, egg, butter, sugar, chicken, cream) |
| Onion Soup | onions, salt (null), garlic |
| Salted Pasta | pasta, salt (null) |
| Veggie Mix | onions, tomato, garlic |

- `usage-count(milk)` = **3**
- `recipesWithMoreThan10Ingredients` = **1** (Big Feast)
- `using milk ≥ 300 ml` = **Pancakes + Cream Sauce**, not Big Feast (15 ml)
- Pantry `{milk 480 ml, salt, onions 2 piece, pasta 1 piece}` → Salted Pasta **full match**
  (missing 0), Onion Soup missing 1 (garlic), Big Feast missing 9 and **ranked last**
- Semantic probes: "comforting breakfast stack" → Pancakes; "warming winter bowl" → Onion Soup
  (both keyword-disjoint against the whole corpus)

## Checks (17): build, start, seed, CRUD, usage-count, stat, conversion hit+miss, pantry rank
(first = full match, last = Big Feast), two semantic probes, restart persistence.

## Verified seams (koan-side) + the docs gap

- `[Parent]` attribute exists (`Koan.Data.Core.Relationships`) with the relationship query
  subsystem (loader/executor/policy) behind the governed GET (`With=` expansion).
- `BeforeSave`-class hooks exist in the endpoint pipeline — canonical-unit normalization is a
  supported seam.
- **Docs gap (v7 candidate):** the relationship surface is documented only in *archived proposals*
  and the MCP projection comment; no promoted leaf teaches `[Parent]` + `With=` + usage lookups.
  The koan arm's discovery of this surface is itself a measured question in this column.

## Fairness / execution

Task body is byte-identical across arms; only the arm line differs. Port 5097; grading lock
respected. **Execution requires Ollama up** (semantic stage embeds through the app) — do not run
while the GPU is user-reserved. Runner: `run-test03.sh` (single stage, 45-min cap, arm pair
sequential).
