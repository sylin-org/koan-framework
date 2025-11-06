# AI-0017 Fact-Catalog-First Extraction for Meridian Pipelines

**Status:** Accepted  
**Date:** 2025-10-24  
**Owners:** Meridian sample team  
**Supersedes:** None  
**Related:** AI-0006, AI-0014, DATA-0061

## Context

The Meridian sample previously relied on `FieldExtractor` to populate deliverable fields directly from passages retrieved via hybrid vector search. Each field prompt embedded the JSON schema and forced the language model to emit a value per field. This design produced three systemic failures:

- **Frequency bias:** majority terms (e.g., company names) dominated prompts, so person fields were filled with organizations.  
- **Schema coupling:** large schemas inflated prompts, starved evidence tokens, and hid nuanced facts.  
- **Opaque behaviour:** debugging mis-extractions was hard because there was no intermediate representation of the sourced facts.

We need a reversible, reviewable pipeline that mirrors human practice: gather facts first, then map them into the deliverable template. The new approach must also respect configurable taxonomies per analysis type and allow operators to inspect candidate matches before committing.

## Decision

1. **Two-phase processing** replaces direct field extraction:
   - *Phase 1 – Fact Catalog*: `DocumentFactExtractor` enumerates structured facts across documents (people, decisions, technical specs, metrics, etc.), capturing provenance, confidence, and source hierarchy.  
   - *Phase 2 – Field Matching*: `FieldFactMatcher` reasons over catalog entries and analysis-type expectations to select best-fit values per deliverable field, falling back to author review when confidence is inadequate.

2. **Taxonomy on the analysis type:** `AnalysisType` now includes a `FactCategories` collection. Saving or updating an analysis type triggers an AI-assisted taxonomy builder that inspects the output template/schema and suggests categories (keys, descriptions, synonyms, guidance). Authors may adjust the result before publishing.

3. **Persistence:** Facts are stored as first-class `Entity<T>` records (`DocumentFact`) keyed by source document and pipeline, enabling reuse across executions and pipelines. Authoritative Notes are ingested as highest-precedence facts automatically.

4. **UI and review surface:** The admin experience will show, per deliverable field, the ranked candidate facts (with reasoning, provenance, and confidence) so operators can approve, override, or supply missing data.

5. **Telemetry:** Fact extraction and matching emit `RunLog` entries with prompt hashes, selected facts, and confidence bands, preserving an audit trail and enabling quality diagnostics.

## Consequences

- **Quality:** Separating fact discovery from schema binding eliminates frequency bias and allows semantic checks (“field expects person”) before acceptance.  
- **Maintainability:** Analysis types control their own taxonomies; template updates regenerate categories via LLM, so no hard-coded routing tables are required.  
- **Performance:** Fact extraction occurs per document change and is cached; matching reuses cached facts unless the taxonomy or documents change. Removal of token budgets shifts responsibility to provider limits, so long prompts must still consider platform constraints.  
- **Operator workflow:** Reviewers gain visibility into both the fact catalog and the matching rationale, enabling manual intervention without rerunning the entire pipeline.  
- **Code impact:** `FieldExtractor` and supporting prompt logic will be retired in favour of the new services, with greenfield implementations for fact extraction, matching, persistence, and UI integration.

## Next Steps

- Implement `DocumentFactExtractor`, `DocumentFact`, and `FieldFactMatcher`.  
- Update pipeline orchestration to call the fact catalog workflow and retire legacy extraction code.  
- Extend admin UI to surface candidate facts per field.  
- Add regression coverage for fact caching, taxonomy generation, and low-confidence review flows.
