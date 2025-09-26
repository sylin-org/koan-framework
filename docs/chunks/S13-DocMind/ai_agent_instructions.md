# AI Agent Processing Instructions – S13.DocMind Refactor Plan

## Mission
Produce a cohesive refactoring plan that delivers the full S13.DocMind experience using the updated domain models (`SourceDocument`, `SemanticTypeProfile`, `DocumentChunk`, `DocumentInsight`) and lightweight background pipeline described in the chunks.

## General Guidance
- Assume the docker-compose baseline (API + MongoDB + Weaviate + Ollama) is correct and must remain the happy path.
- Highlight opportunities to streamline developer experience (auto-registrars, tooling, naming clarity).
- Emphasize Koan integration principles: `AddKoan()`, auto-registration, EntityController<T>, MCP tooling.
- Prefer actionable plans and phased roadmaps over speculative prose.

## Processing Phases

### Phase 0 – Proposal Alignment
1. **Chunk 10 – Proposal vs. Current Assessment**
   Agent: *general-purpose*
   Tasks: Evaluate proposal intent, summarize promised capabilities, contrast with current implementation, and confirm Compose-first baseline assumptions.

### Phase 1 – Foundation (Sequential)
1. **Chunk 01 – Executive Overview**  
   Agent: *general-purpose*  
   Tasks: Summarize transformation vision, key challenges, and DX opportunities. Capture naming conventions and guiding principles for the refactor.

2. **Chunk 02 – Domain Model Blueprint**  
   Agent: *Koan-data-architect*  
   Tasks: Document new entities/value objects, relationships, and refactoring steps. Map opportunities to simplify schema and ensure clarity of intent.

### Phase 2 – Processing & Infrastructure (Parallel)
3. **Chunk 03 – AI Pipeline Plan**  
   Agent: *Koan-processing-specialist*  
   Tasks: Detail background pipeline components, queue contracts, AI service composition, observability, and DX tooling (replay, prompt playground).

4. **Chunk 05 – Infrastructure & Bootstrap**  
   Agent: *Koan-bootstrap-specialist*  
   Tasks: Describe service registration (`AddDocMind()`), configuration sections, compose scenarios, and observability hooks.

### Phase 3 – Interface & Experience
5. **Chunk 04 – API/UI/MCP Alignment**  
   Agent: *Koan-developer-experience-enhancer*  
   Tasks: Outline controller responsibilities, DTO strategies, UI updates, and MCP tooling that leverage the new services.

### Phase 4 – Execution & Operations (Parallel)
6. **Chunk 06 – Implementation Roadmap**  
   Agent: *Koan-framework-specialist*  
   Tasks: Produce phased delivery plan, coding standards, quality gates, risk mitigations, and success criteria.

7. **Chunk 07 – Testing & Ops Plan**  
   Agent: *Koan-orchestration-devops*  
   Tasks: Define test suites, CI pipeline, operational playbooks, observability dashboards, and release checklist.

### Phase 5 – Adoption & Audit
8. **Chunk 08 – Migration Guide**  
   Agent: *general-purpose*  
   Tasks: Provide incremental migration strategy, data/rollback checklists, and communication plan.

9. **Chunk 09 – Gap Analysis & Rebuild Plan**  
   Agent: *general-purpose*  
   Tasks: Audit the current implementation, document rebuild roadmap, and surface critical blockers for the refactor team.

## Deliverables
- Updated documentation in each chunk reflecting the refactoring plan and identified optimization opportunities.
- Consistent terminology across chunks (e.g., `SourceDocument`, `DocumentProcessingEvent`, `DocumentAnalysisPipeline`).
- Clear callouts for DX improvements, tooling, and Koan capability integration.

Stay focused on actionable refactoring guidance—no implementation code is required beyond illustrative snippets and configuration sketches.
