# ANL-6 · Explanation, ledger, shape, and freshness negotiation — the facts the surface already owns

> **Tier**: T3 · **Depends on**: ANL-3, ANL-5 · **Normative decision**: [DATA-0123](../../../decisions/DATA-0123-embedded-analytics-and-duckdb-connector.md)
> Self-contained session prompt — paste into a fresh session. The **call-site rule** and the
> honesty commitments in DATA-0123 are acceptance criteria, not flavor. Update
> [../OPEN-ITEMS.md](../OPEN-ITEMS.md) and [../README.md](../README.md) when done.

---

## Why this exists

Four daily moments from the delight matrix remained ★★ because their facts exist but no door
exposes them: *why is this number what it is*, *what has refreshing cost me*, *what shape is the
answer before I compute it*, and *I accept answers this old — no older*. None of these introduces
a new concept; each exposes what the declaration, the envelope, or the refresh state already
knows. That is the whole card: pure exposure, envelope-first, one honesty rule per door.

## Mission

### 1. `GET {recipe}/explain` — the explanation door (no compute, ever)

Composes without executing: engine elected, would the ask **serve** or **compute** (or **refuse**,
with the corrective that execution would have raised), the composed SQL with its bound parameters,
declared vs. supplied parameters, bounds, projection policy, materialization state (last refresh,
row count, duration), and the elected sink's capabilities (`facets`, `delta`, `parquet`).
Side-effect law: explain never computes, never writes rows, never creates a projection table — a
never-refreshed projection still reads as never-refreshed after an explain. MCP mirror:
`analytics.explain`.

### 2. `GET {recipe}/history` — the refresh ledger

Every re-materialization appends a ledger entry: when, row count, duration, and **trigger**
(`loop` | `http` | `programmatic` | `backfill-on-read`). Bounded ring per recipe; newest first.
Delight: "stale or broken" becomes one call, and the refresh-cost curve is an operator fact. The
ledger writes in the same transaction as the refresh stamp — a refresh without its history entry
is an adapter defect. On-demand questions refuse (they have nothing to look back on). MCP mirror:
`analytics.history`.

### 3. `GET {recipe}/shape` — the answer without the compute

Output columns with CLR types, declared parameters by name and type, measure kind, group member,
bounds, materialized-or-not, and the projection policy. Pure catalog read — no sink, no compute,
works for on-demand questions (with `Materialized: false` saying which doors apply). Agents and
grid components bind before asking. MCP mirror: `analytics.shape`.

### 4. Freshness negotiation — `?maxAge=` and the materialization's HTTP caching headers

`GET {recipe}?maxAge=15m` states the caller's tolerance for this ask: a materialization within it
is served; anything older computes live (labeled so, backfill per policy). Durations parse as
`90s` / `15m` / `2h` / `1d` or plain seconds; malformed or negative refuses; `maxAge` on an
on-demand question refuses (live is always age zero — the parameter would lie). The served answer
gains `MaterializedUtc`, and the door derives HTTP caching from it: `ETag` over
(question, bounds, parameters, refresh stamp), `Last-Modified` from the stamp, and
`Cache-Control: no-cache` so pollers revalidate against the ETag and take 304s — correctness
guaranteed, no staleness math in downstream caches. Live answers get no caching headers.

## Honesty commitments realized

- `explain` is side-effect-free and says `would serve / would compute / would refuse` — never
  guesses silently.
- The ledger's trigger column names what caused every re-materialization; a hosted loop can no
  longer refresh in secret.
- `shape` never computes; `Materialized: false` is the flag that refuses the row doors.
- `maxAge` either changes the served path visibly (envelope says which) or refuses — never
  silently ignored.

## Acceptance

- Specs pin: shape for materialized and on-demand questions; explain's serve/compute/refuse
  outcomes, capability list, and the no-side-effect law; ledger entries with triggers and order;
  maxAge serve-vs-live flip and the on-demand refusal; ETag + 304 revalidation through the
  controller with a bare `DefaultHttpContext`.
- Full suite green; recipe and guide updated; OPEN-ITEMS delight-doors row closed.
