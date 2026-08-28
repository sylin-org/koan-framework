# ANL-5 · Facet and delta doors — distribution, movement, and the change cursor

> **Tier**: T3 · **Depends on**: ANL-3 (projections) · **Normative decision**: [DATA-0123](../../../decisions/DATA-0123-embedded-analytics-and-duckdb-connector.md)
> Self-contained session prompt — paste into a fresh session. The **call-site rule** and the
> honesty commitments in DATA-0123 are acceptance criteria, not flavor. Update
> [../OPEN-ITEMS.md](../OPEN-ITEMS.md) and [../README.md](../README.md) when done.

---

## Why this exists

Dashboard builders need filter dropdowns without declaring a recipe per facet, and incremental
consumers (agents, sync jobs, live dashboards) need "what changed since I last looked" without
pulling the whole materialization. Both are read-model shapes over projection tables the sink
already owns — this card exposes them as doors with the same honesty contract as the rest of
the surface.

## The two questions, kept distinct

The design dialogue that produced this card drew one line deliberately:

- **Facets without `since` answer "what is the distribution?"** — every materialized row, full
  picture. Filter dropdowns, agent world-knowledge.
- **Facets with `since` answer "what has been moving?"** — an activity summary over changed
  rows, not a distribution. A cached pie chart cannot be updated from it: a row that moved
  Open→Done contributes one `Done` count, and the `-1` half is unknowable from stamps.

One door, honest mode flip; the envelope names which question it answered.

## Mission

### Sequenced build (each step lands green before the next)

1. **Distribution facets** — `GET {recipe}/facets?by=column&limit=` (and
   `Analytics.Facets(name, by)`): distinct values with counts for any declared projection
   column, engine-side GROUP BY, value-ordered by count descending. Materialized questions
   only; on-demand questions refuse naming why. Undeclared columns refuse listing the declared
   set. Bucket-capped answers say so (`Completion: RowCapped`).
2. **Watermark machinery** — the sink gains a per-row change stamp (`_koan_stamp`, unix
   milliseconds of the writing refresh). Refreshes rewrite wholesale, so every re-materialized
   row carries the refresh's stamp — "changed since W" means "written by a materialization
   after W". The stamp is operational: never a declared column, stripped from every row the
   doors return.
3. **The delta door** — `GET {recipe}/delta?since=&limit=` (and `Analytics.Delta(name, since)`):
   materialized rows written after the watermark, plus the **next watermark on every response**
   (`Watermark: { given, current }`). Consumers never construct watermarks — the door hands
   back the cursor; the consumer holds it, the server keeps no per-consumer state. Watermarks
   are opaque (`wm1.<unixMs>`); a malformed one refuses loudly instead of being coerced to zero.
4. **Movement facets** — `GET {recipe}/facets?by=column&since=<watermark>` falls out almost
   free: the same GROUP BY with a stamp predicate. The envelope adds `Mode: movement`,
   `ChangesConsidered` (rows the movement summarizes — so `Done×30` cannot be misread as
   "30 total Done"), the handed-back watermark, and `DeletesInvisible: true` stated, because a
   deleted source row leaves no trace in a derived store's stamps.

### Envelope law

Every door answers with provenance: which mode ran, what the cursor is, what the counts cover,
and what the answer cannot see. Refusals name the missing capability ("this sink does not track
changes") rather than degrading silently.

### Surface parity

The module surface (`Analytics.Facets`, `Analytics.Delta`) and the MCP tools
(`analytics.facets`, `analytics.delta`) speak the identical doors — HTTP, code, and agents
share one vocabulary. Free-form SQL remains refused everywhere.

### Engine capability shape

Distribution facets join `IAnalyticsProjectionSink` (any tabular sink can GROUP BY). Change
tracking is an optional capability, `IAnalyticsChangeTracking` — the same pattern as
`IAnalyticsParquetExport`: doors advertise what the elected engine actually offers and refuse
what it does not, so a future engine without stamps degrades loudly, not wrongly.

## Honesty commitments realized

- A movement answer states that updates count once at their new value and that deletions are
  invisible (`DeletesInvisible`), never implying it is a distribution.
- `ChangesConsidered` accompanies every movement answer.
- Capped facet answers (`limit`) state the cap.
- Watermark handling is fail-closed: malformed cursors refuse with the expected shape, never
  silently rewind to the beginning of time.

## Acceptance

- Specs pin: distribution buckets, on-demand refusal, undeclared-column refusal, bucket cap,
  delta cursor hand-back and advance across refreshes, movement envelope fields, malformed
  watermark refusal.
- Full suite green; the rows door is unaffected by the stamp column (stripped reads pinned by
  the existing row-shape tests).
- Recipe and guide updated with the two doors; OPEN-ITEMS ledger reflects the closures.
