# OFFER-0001: Offering Taxonomy, Tags, and Query-Based Recommendations

**Status:** Accepted  
**Date:** 2026-01-17  
**Context:** Users often know the *kind* of service they want (e.g., “database”, “document store”, “vector DB”) but not the specific curated offering name. We also want better success UX when an install is incompatible with a given stone.

---

## Problem

- `garden-rake offer <token>` currently assumes `<token>` is an offering name and attempts installation.
- Users want discovery by intent (category/tags) and ranked suggestions, not an eager install.
- In multi-stone environments, users want recommendations that can pick a *better stone* (e.g., prefer SSD) without making the preference a hard filter.
- We already have curated offerings with categories, but we lack structured tags and synonym normalization.

---

## Decision

### 1) Each offering has a single category

- Category is a single, stable identifier (e.g., `data`, `vector`, `messaging`, `secrets`, `observability`, `cache`).
- Category is authoritative metadata owned by the offering (via frontmatter).

### 2) Offerings may have tags

- Tags are short, lowercase tokens describing capabilities and usage intent (e.g., `database`, `document`, `sql`, `nosql`, `search`, `queue`, `inference`).
- Tags are used for:
  - query-mode recommendations
  - future “alternatives” suggestions when an install fails

### 3) Central synonym dictionary

- A repository-owned dictionary maps common user tokens to canonical tokens.
- It powers query normalization so inputs like `db`, `doc`, `mq`, `fts` map to canonical tags/categories.

### 4) `garden-rake offer` adds query mode

When `garden-rake offer <arg>` is invoked:

- If `<arg>` matches a known offering name on the targeted stone, proceed with installation (existing behavior).
- Otherwise treat `<arg>` as a **query** and print **top 3 ranked recommendations**.

Query tokens:
- Comma-separated and/or whitespace separated (e.g., `database,document`)
- Normalized through the synonym dictionary

Ranking:
- Score uses category + tag matches (category matches outrank tag matches).
- Compatibility `fail` entries are not recommended.
- Output includes copy/paste commands.

### 4.1) Install failures suggest alternatives

If an install attempt fails due to `COMPATIBILITY_FAILED`, rake prints top alternatives derived from the offering's own tags/category, and includes copy/paste commands.

Optional: if the user passes `--anywhere-on-fail`, rake also runs an automatic `--at anywhere` recommendation pass using the same derived intent.

### 5) `--prefer` is strong but non-blocking

- `--prefer <token>` biases ranking but does not hard-exclude candidates.
- First implementation focuses on stone-level prefers (e.g., disk type: SSD/NVMe/HDD).

### 6) `--at anywhere` ranks across stones

- `--at anywhere` triggers network-wide stone discovery.
- Rake evaluates offerings per stone and prints the global top 3 `(stone, offering)` recommendations.
- Output includes explicit `--at <endpoint>` commands so the user can run an install deterministically.

---

## Data Sources

- Offering metadata: `*.frontmatter.json` alongside offering manifests.
- Synonyms: `manifests/taxonomy.dictionary.yaml` (canonical mapping).
- Stone capabilities for prefer scoring: `GET /capabilities`.

---

## API/Compatibility Notes

- Moss continues to evaluate compatibility per stone and marks offerings as `pass` / `fallback` / `fail`.
- Rake recommendation logic excludes `fail` offerings from suggestions.

---

## Consequences

- Adds a discoverability UX without changing install semantics for known offering names.
- Requires tags to be curated (lightweight ongoing maintenance).
- Disk type detection is best-effort; unknown disk type results in no prefer boost.

---

## Implementation Checklist

- Add tags to all curated offerings frontmatter.
- Surface tags through moss compiled offerings index and `/api/offerings` response.
- Implement query mode in rake (`offer <query>`).
- Add `--prefer` and `--at anywhere` semantics.
