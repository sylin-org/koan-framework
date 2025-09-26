# AI Agent Processing Instructions for S13-DocMind Chunks

## Overview

This document provides detailed instructions for AI agents processing the chunked S13-DocMind proposal. Each chunk has been designed for semantic coherence and optimal agent specialization.

## Prerequisites

**Before processing any chunks, ensure you understand:**
- Koan Framework core principles ("Reference = Intent", Entity-first development)
- Multi-provider data architecture patterns
- Auto-registration and bootstrap concepts
- Flow/Event Sourcing integration patterns

## Processing Workflow

### Phase 1: Foundation Analysis (Sequential)

#### Step 1: Strategic Overview Analysis
```bash
# Process Chunk 1 with general-purpose agent
Agent: general-purpose
File: 01_executive_overview.md
Focus: Extract transformation goals and architectural principles
```

**Agent Instructions:**
- Identify key transformation goals from traditional .NET to Koan Framework
- Extract architectural challenges that drove the redesign
- Define success criteria for the transformation
- Map original solution capabilities that must be preserved

**Expected Outputs:**
- `transformation_goals.md` - Clear objectives for the migration
- `architectural_principles.md` - Koan Framework patterns to apply
- `success_criteria.md` - Measurable outcomes for validation

#### Step 2: Entity Design Authority
```bash
# Process Chunk 2 with Koan data architect specialist
Agent: Koan-data-architect
File: 02_entity_models.md
Focus: Entity<T> patterns, relationships, provider assignments
```

**Agent Instructions:**
- Extract complete entity specifications (File, Type, Analysis entities)
- Document Parent/Child relationships and their implications
- Identify DataAdapter assignments and provider optimization strategies
- Map entity lifecycle states and transitions

**Expected Outputs:**
- `entity_specifications.cs` - Complete entity class definitions
- `entity_relationships.md` - Relationship mapping and navigation patterns
- `provider_strategy.md` - Multi-provider assignment rationale

### Phase 2: Parallel Specialization

#### Step 3A: AI Processing Architecture
```bash
# Process Chunk 3 with Koan flow specialist
Agent: Koan-flow-specialist
File: 03_ai_processing.md
Prerequisites: Chunks 1, 2 completed
Focus: Flow patterns, AI integration, event sourcing
```

**Agent Instructions:**
- Design Flow command specifications for background processing
- Map AI.Prompt() and AI.Embed() integration patterns
- Define event sourcing projections for document analysis workflows
- Specify AI model management service architecture

**Expected Outputs:**
- `flow_commands.cs` - Background processing Flow definitions
- `ai_service_specs.md` - DocumentIntelligenceService architecture
- `event_projections.md` - Event sourcing integration patterns

#### Step 3B: Infrastructure Foundation
```bash
# Process Chunk 5 with Koan bootstrap specialist (parallel with 3A)
Agent: Koan-bootstrap-specialist
File: 05_infrastructure.md
Prerequisites: Chunks 1, 2 completed
Focus: Auto-registration, provider configuration, bootstrap reporting
```

**Agent Instructions:**
- Design KoanAutoRegistrar implementation for S13.DocMind
- Specify multi-provider configuration strategy
- Define bootstrap reporting structure
- Map provider election and capability detection logic

**Expected Outputs:**
- `auto_registrar.cs` - Complete KoanAutoRegistrar implementation
- `provider_config.md` - Multi-provider setup specifications
- `boot_report.md` - Bootstrap reporting structure

### Phase 3: Interface Design

#### Step 4: Developer Experience Authority
```bash
# Process Chunk 4 with Koan developer experience enhancer
Agent: Koan-developer-experience-enhancer
File: 04_api_ui_design.md
Prerequisites: Chunks 1, 2, 3 completed
Focus: EntityController<T> patterns, API generation, user workflows
```

**Agent Instructions:**
- Extract EntityController<T> specifications for each entity type
- Document auto-generated API surface and customization patterns
- Map user workflows from upload through analysis completion
- Define frontend integration patterns and UI component specifications

**Expected Outputs:**
- `entity_controllers.cs` - Complete API controller specifications
- `api_surface.md` - Auto-generated endpoint documentation
- `user_workflows.md` - End-to-end user journey mapping
- `ui_components.md` - Frontend integration specifications

**Sub-chunk Processing (for large contexts):**
```bash
# If Chunk 4 exceeds context limits, process as sub-chunks:
# 4A: API Controllers (lines 443-720)
# 4B: UI Components (lines 721-980)
# 4C: Performance (lines 981-1165)
```

### Phase 4: Implementation Specification

#### Step 5: Framework Compliance Authority
```bash
# Process Chunk 6 with Koan framework specialist
Agent: Koan-framework-specialist
File: 06_implementation.md
Prerequisites: All previous chunks completed
Focus: Implementation roadmap, performance specs, security compliance
```

**Agent Instructions:**
- Validate all specifications against Koan Framework principles
- Extract concrete implementation requirements and acceptance criteria
- Define performance benchmarks and optimization strategies
- Specify security patterns and compliance requirements

**Expected Outputs:**
- `implementation_roadmap.md` - Phase-by-phase implementation plan
- `performance_benchmarks.md` - Concrete performance targets
- `security_specifications.md` - Security patterns and compliance requirements
- `framework_compliance.md` - Validation against Koan principles

#### Step 6: Deployment Authority (Parallel with Step 5)
```bash
# Process Chunk 7 with Koan orchestration DevOps specialist
Agent: Koan-orchestration-devops
File: 07_testing_ops.md
Prerequisites: Chunks 1-5 completed
Focus: Container orchestration, testing strategies, operational procedures
```

**Agent Instructions:**
- Extract Docker Compose configurations and container orchestration patterns
- Document testing strategies and integration test requirements
- Define health monitoring and observability specifications
- Specify deployment procedures following S5/S8 sample patterns

**Expected Outputs:**
- `docker_configurations/` - Complete container orchestration setup
- `testing_strategy.md` - Comprehensive testing approach
- `monitoring_specs.md` - Health checks and observability
- `deployment_procedures.md` - Operational runbooks

### Phase 5: Cross-Reference Integration

#### Step 7: Migration Pattern Extraction
```bash
# Process Chunk 8 throughout all phases as reference material
Agent: general-purpose
File: 08_migration_guide.md
Usage: Cross-reference material for all other chunks
Focus: Code transformation patterns, reusable components, troubleshooting
```

**Agent Instructions:**
- Extract code transformation patterns that apply to chunks 2-7
- Document reusable component mappings from original to Koan patterns
- Create troubleshooting procedures for common migration issues
- Maintain cross-reference index for pattern reuse

**Expected Outputs:**
- `transformation_patterns.md` - Code migration pattern catalog
- `component_mapping.md` - Original → Koan component mappings
- `troubleshooting_guide.md` - Common issues and solutions
- `pattern_index.md` - Cross-reference guide for all chunks

## Agent Coordination Protocols

### Coordination Checkpoints

#### Checkpoint 1: Entity Design Review
**Participants:** Koan-data-architect, Koan-flow-specialist
**Trigger:** After chunks 2 and 3 completion
**Deliverable:** Validated entity specifications with Flow integration

```bash
# Review process:
1. Data architect presents entity specifications
2. Flow specialist validates against Flow/Event Sourcing patterns
3. Joint resolution of any conflicts
4. Produce unified entity + flow specifications
```

#### Checkpoint 2: API Workflow Alignment
**Participants:** Koan-developer-experience-enhancer, Koan-flow-specialist
**Trigger:** After chunks 3 and 4 completion
**Deliverable:** Integrated API and Flow specifications

```bash
# Review process:
1. DX enhancer presents API surface and user workflows
2. Flow specialist validates background processing integration
3. Align user-triggered events with Flow command processing
4. Produce integrated API + Flow workflow specifications
```

#### Checkpoint 3: Deployment Readiness
**Participants:** Koan-orchestration-devops, Koan-framework-specialist
**Trigger:** After chunks 6 and 7 completion
**Deliverable:** Production deployment plan

```bash
# Review process:
1. DevOps presents deployment configurations and procedures
2. Framework specialist validates against compliance requirements
3. Joint resolution of security and operational concerns
4. Produce production-ready deployment plan
```

## Quality Assurance Guidelines

### Output Validation Requirements

**For Entity Specifications:**
- Must follow Entity<T> or Entity<T,K> patterns
- Include proper DataAdapter attributes
- Define clear Parent/Child relationships
- Specify provider optimization strategies

**For Flow Specifications:**
- Must integrate with Entity<T> patterns
- Include proper event sourcing projections
- Define clear command/event boundaries
- Specify error handling and retry policies

**For API Specifications:**
- Must extend EntityController<T> base classes
- Include auto-generated endpoint documentation
- Define custom action specifications
- Specify authentication and authorization patterns

**For Infrastructure Specifications:**
- Must include complete KoanAutoRegistrar implementation
- Define multi-provider configuration strategy
- Include bootstrap reporting specifications
- Specify health monitoring and readiness checks

### Cross-Chunk Consistency Validation

**Entity Consistency:**
- Entity definitions in Chunk 2 must match usage in Chunks 3, 4, 6
- Provider assignments must align across all technical chunks
- Relationship patterns must be consistent in all implementations

**Flow Integration:**
- Flow commands in Chunk 3 must align with API triggers in Chunk 4
- Event sourcing patterns must match entity lifecycle in Chunk 2
- Background processing must align with deployment specs in Chunk 7

**Framework Compliance:**
- All specifications must follow Koan Framework principles
- Auto-registration patterns must be consistent across all components
- Provider transparency must be maintained in all data access patterns

## Error Handling and Escalation

### Common Processing Issues

**Context Window Limitations:**
- Use sub-chunk processing for Chunks 4, 6, 7
- Prioritize key concept extraction over implementation details
- Maintain cross-references between sub-chunks

**Specification Conflicts:**
- Escalate to coordination checkpoints
- Document conflicts in `conflicts.md` for resolution
- Use Chunk 8 migration patterns as conflict resolution guide

**Framework Compliance Violations:**
- Consult Koan Framework specialist for authoritative resolution
- Document deviations with explicit justification
- Update specifications to maintain framework alignment

### Escalation Matrix

**Level 1 - Agent Self-Resolution:**
- Minor specification gaps or implementation details
- Cross-reference with Chunk 8 migration patterns
- Document assumptions and continue processing

**Level 2 - Coordination Checkpoint:**
- Conflicts between chunks requiring multi-agent resolution
- Major architectural decisions affecting multiple domains
- Integration patterns requiring specialist consensus

**Level 3 - Framework Authority:**
- Fundamental conflicts with Koan Framework principles
- Security or compliance specification gaps
- Performance requirement conflicts with framework capabilities

## Success Criteria

### Individual Chunk Success
- Complete extraction of all key concepts and specifications
- Framework-compliant outputs following Koan patterns
- Clear documentation of dependencies and relationships
- Actionable implementation guidance

### Coordinated Success
- Consistent entity and API specifications across all chunks
- Integrated Flow and infrastructure patterns
- Production-ready deployment configurations
- Complete migration guidance with troubleshooting procedures

### Overall Project Success
- Fully specified S13.DocMind implementation following Koan Framework patterns
- Clear transformation path from original solution to Koan-native architecture
- Reusable patterns applicable to similar AI-native document intelligence solutions
- Complete operational procedures for deployment and maintenance