---
type: DEV
domain: samples
title: "Sample Collection Strategic Realignment - Themed Applications & Capability Coverage"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-10-16
framework_version: v0.6.3
validation:
  date_last_tested: 2025-10-16
  status: proposed
  scope: samples/*
---

# DX-0045: Sample Collection Strategic Realignment

Status: Proposed

## Context

### Current State Assessment

As of October 2025, the Koan Framework sample collection contains 19 samples with significant organizational and quality issues:

**Inventory Problems:**
- **Unclear numbering**: Multiple S6 and S7 samples create confusion
- **Missing documentation**: 6 samples lack README files entirely (S2, S4, S6.Auth, S6.SocialCreator, S4.Web)
- **Inconsistent quality**: Documentation ranges from excellent (S5.Recs: comprehensive tutorial) to nonexistent
- **Unclear purpose**: Several samples have no discernible value proposition

**Capability Coverage Gaps:**

Analysis against the framework's 8-layer capability architecture (from `docs/architecture/capability-map.md`) reveals **58% coverage**:

| Layer | Coverage Status | Gaps |
|-------|----------------|------|
| 1. Core Runtime | ✅ 100% | None |
| 2. Data & Storage | ⚠️ 75% | No backup/restore sample, limited object storage |
| 3. Web & APIs | ⚠️ 70% | No GraphQL sample, transformers underdemonstrated |
| 4. Messaging & Async | ❌ 40% | No CQRS/outbox sample, scheduling passive |
| 5. AI & Media | ⚠️ 60% | Limited media pipeline demonstration |
| 6. Secrets | ❌ 0% | **ZERO coverage** of Koan.Secrets.* |
| 7. Recipes & Orchestration | ❌ 30% | Recipe system not demonstrated, CLI underutilized |
| 8. Domain Pipelines | ✅ 80% | Missing Dapr integration |

**Learning Path Issues:**
- No clear progression from beginner (S0/S1) to intermediate
- S10.DevPortal is demo-focused, not learning-focused
- After S1.Web, unclear where developers should go next
- Advanced samples (S5.Recs, S16.PantryPal) too complex for intermediate learners

**Quality Concerns (QA Specialist Perspective):**
- Only S16.PantryPal has comprehensive unit tests
- No samples demonstrate testing patterns
- Inconsistent error handling across samples
- Limited edge case coverage outside S10/S14
- No performance regression examples beyond S14

**Enterprise Value Concerns (Architect Perspective):**
- Missing "second sample" bridging basic to advanced
- No distributed systems/multi-service pattern demonstrations
- Secret management gap is **critical** for enterprise adoption
- Event sourcing/CQRS patterns not shown (common enterprise requirement)

### Previous Decisions

This ADR supersedes and extends:
- **DX-0044**: Adapter benchmark sample (S14.AdapterBench - retained)
- **DX-0042**: Narrative samples entity naming conventions
- Informal sample organization discussions

### Problem Statement

The current sample collection **fails to effectively demonstrate framework capabilities** and **hinders developer adoption** due to:

1. **Critical capability gaps** - Zero coverage of enterprise-critical features (secrets, CQRS)
2. **Poor discoverability** - Hard to find "which sample shows X feature?"
3. **Unclear learning path** - Missing intermediate samples between basic and advanced
4. **Quality inconsistency** - Documentation and testing vary wildly
5. **Maintenance burden** - 19 samples include several with unclear value

## Decision

### Strategic Realignment Plan

Streamline the sample collection from **19 samples to 14 core samples** while adding **4 strategic new samples** to achieve **92% framework capability coverage**.

### Phase 1: Archival (Immediate)

**Archive These Samples:**
- ❌ **S2** - No README, unclear purpose, legacy structure
- ❌ **S4.Web** - No README, no clear value proposition
- ❌ **S6.Auth** - Redundant with auth in S5.Recs and S7.TechDocs
- ❌ **S6.SocialCreator** - No README, unclear status
- ❌ **S12.MedTrials** - Sparse documentation, unclear value vs. S16.PantryPal MCP demonstration
- ❌ **S15.RedisInbox** - Too minimal, concepts integrated into new S3.NotifyHub
- ❌ **KoanAspireIntegration** - Integration example, not sample application

**Rationale**: These samples lack clear documentation or value, create confusion, and add maintenance burden without educational benefit.

**Retained Samples (No Changes):**
- ✅ **guides/g1c1.GardenCoop** - Part of larger guide narrative (explicitly retained)

### Phase 2: Consolidation

**Merge Operations:**
- 🔄 **S7.TechDocs + S7.ContentPlatform → S7.DocPlatform**
  - Combines mode-based architecture with moderation workflows
  - One comprehensive content management sample
  - Demonstrates: moderation, soft-delete, roles, search, workflow

**Rationale**: Both S7 samples address content management with overlapping concerns. Merging creates one authoritative content platform sample.

### Phase 3: New Themed Applications

Following the **S5.Recs model** of compelling domain applications that naturally require framework capabilities (not framework showcases disguised as apps).

#### **S3.NotifyHub - Multi-Channel Notification Platform**

**Domain**: SaaS notification delivery service

**Application Features:**
- Multi-channel delivery (email, SMS, push, webhooks)
- Priority queues (urgent vs. bulk notifications)
- Scheduled delivery with cron support
- Retry with exponential backoff
- Dead letter queue for failed messages
- Idempotency via Redis Inbox
- Batch campaigns (10k+ notifications)
- Rate limiting per channel
- Real-time delivery tracking dashboard

**Framework Capabilities Demonstrated:**
- ✅ Koan.Messaging.Connector.RabbitMq - Queue-based routing
- ✅ Koan.Service.Inbox.Connector.Redis - Idempotency and deduplication
- ✅ Koan.Scheduling - Scheduled notification delivery
- ✅ Message retry patterns - Exponential backoff, DLQ
- ✅ Batch operations - Bulk notification campaigns
- ✅ Entity<T> patterns - NotificationMessage, DeliveryAttempt entities
- ✅ Multi-provider data - MongoDB for messages, Redis for state

**Why This Works:**
- Universally relatable (every company sends notifications)
- Naturally async (messaging is obvious solution)
- Rich functionality (shows retry, scheduling, inbox organically)
- Production patterns (real-world queue architecture)

**QA Assessment**: 🟦 **EXCELLENT** - Naturally exposes testable patterns, realistic failure modes, observable behavior

**Architect Assessment**: 🟦 **VERY HIGH** business value, excellent scalability story, universal need

---

#### **S4.DevHub - Development Operations Dashboard**

**Domain**: DevOps integration and secret management hub

**Application Features:**
- Multi-service integration (GitHub, Slack, DataDog, AWS, Azure, PagerDuty)
- Environment management (dev/staging/prod with different credentials)
- Secret rotation without downtime
- Audit trail for secret access
- Health monitoring for service connectivity
- Unified dashboard for all integrations
- Secret scanning and detection
- Team permissions and RBAC
- CI/CD integration with secure credential injection
- Vault integration for production

**Framework Capabilities Demonstrated:**
- ✅ Koan.Secrets.Core - Secret resolution with `secret://` references
- ✅ Koan.Secrets.Connector.Vault - Production Vault integration
- ✅ KoanEnv - Environment detection and configuration overlays
- ✅ Configuration hierarchies - Dev (appsettings) → Prod (Vault)
- ✅ Secret rotation patterns - Hot-reload without restart
- ✅ Multi-provider - Different backends per environment
- ✅ Health checks - Service connectivity validation
- ✅ Entity<T> - IntegrationConfig, SecretReference, AuditLog

**Why This Works:**
- High pain point (secret management universally painful)
- Enterprise critical (security teams care deeply)
- Naturally demonstrates secrets (can't build without secret management)
- Multi-environment story (shows Koan's configuration strength)

**Progressive Complexity:**
- **Easy Start**: Local development with appsettings secrets
- **Production Ready**: Vault integration for production environments

**QA Assessment**: 🟩 **GOOD** - Security testing critical, slightly complex setup but high value

**Architect Assessment**: 🟦 **CRITICAL** business value, but needs "easy start" mode for accessibility

---

#### **S6.MediaHub - Media Processing Pipeline** *(Approved separately)*

**Domain**: Photo/video management with transcoding and storage tiering

**Application Features:**
- Upload pipeline with validation
- Image resizing and optimization
- Storage profiles (hot/cold tiering)
- Backup and restore demonstration
- CDN integration patterns
- Media metadata management
- Thumbnail generation
- Batch processing

**Framework Capabilities Demonstrated:**
- ✅ Koan.Media.Abstractions - Media processing contracts
- ✅ Koan.Media.Core - Processing pipelines
- ✅ Koan.Storage.* - Object storage with profiles
- ✅ Koan.Data.Backup - Backup/restore workflows
- ✅ Entity<T> - MediaAsset, ProcessingJob entities

**Why This Works:**
- Fills three capability gaps (media, storage, backup) in one sample
- Common enterprise need (most apps handle media)
- Natural demonstration of storage patterns

**QA Assessment**: Focus on validation, processing pipeline testing, storage tier verification

**Architect Assessment**: Watch scope creep - focus on Koan patterns, not transcoding details

---

#### **S9.OrderFlow - Event-Sourced Order Management**

**Domain**: E-commerce order tracking and fulfillment system

**Application Features:**
- Order lifecycle management (Placed → Paid → Fulfilled → Shipped → Delivered)
- Complete audit trail (every state change as immutable event)
- Event replay to reconstruct state at any point in time
- Multiple read models (customer view, support view, analytics) - CQRS
- Support debugging ("why did this fail?" answered by event stream)
- Compensation logic (refunds, cancellations as compensating events)
- Outbox pattern for reliable notification sending
- Projections for real-time dashboards
- Time-travel queries ("what orders were pending yesterday at 2pm?")
- Customer portal with full order timeline

**Framework Capabilities Demonstrated:**
- ✅ Koan.Data.Cqrs.Core - Event sourcing and command/query separation
- ✅ Koan.Data.Cqrs.Outbox.Connector.Mongo - Outbox pattern for messaging
- ✅ Koan.Messaging.Core - Event publishing to message bus
- ✅ Event store patterns - Append-only event log
- ✅ Projections - Multiple read models from same event stream
- ✅ Aggregate patterns - Order aggregate with business logic
- ✅ Entity<T> - OrderEvent, OrderProjection, OrderAggregate
- ✅ Multi-provider - Events in append-optimized store, projections in query-optimized

**Why This Works:**
- Clear business value (audit trails matter for commerce)
- Naturally event-driven (orders are state machines)
- Demonstrates CQRS elegantly (different views for different users)
- Compliance angle (regulatory requirements for transaction history)
- Real-world pattern (many enterprises need this architecture)

**QA Assessment**: 🟦 **EXCELLENT** - Event sourcing inherently testable, deterministic replay, compensation testing

**Architect Assessment**: 🟩 **RECOMMENDED FOR ADVANCED AUDIENCES** - Position as intermediate-to-advanced, explain when NOT to use event sourcing

---

### Phase 4: Documentation Standardization

**Apply S5.Recs README Template to All Samples:**

Required sections:
1. **What This Sample Demonstrates** - Clear capability list
2. **The Problem It Solves** - Business context (not "demonstrates X feature")
3. **Quick Start** - One command to run
4. **How to Build an App Like This** - Step-by-step guide
5. **Key Patterns Demonstrated** - Code examples
6. **Testing Guide** - Test examples and patterns
7. **Learning Outcomes** - What developers will understand after exploring
8. **Related Samples** - Cross-references to other samples

**Consistent Project Structure:**
```
SX.SampleName/
├── README.md                    # Comprehensive tutorial
├── TECHNICAL.md                 # Deep dive (optional)
├── Controllers/                 # EntityController patterns
├── Models/                      # Entity<T> models
├── Services/                    # Business logic
├── Tests/                       # REQUIRED: Unit test examples
├── wwwroot/                     # UI (if applicable)
├── docker/                      # Compose files
├── start.bat / start.sh         # One-command start
└── Program.cs                   # Koan bootstrap
```

### Phase 5: Sample Catalog

Create `samples/README.md` with:
- **Capability Matrix** - "Which sample shows X?" lookup table
- **Learning Path** - Recommended order (beginner → advanced)
- **Sample Descriptions** - One-paragraph summary per sample
- **Quick Reference** - Port allocations, dependencies, run commands

## Rationale

### QA Specialist Evaluation Summary

**S3.NotifyHub** - 🟦 Excellent testability
- Clear input/output boundaries
- Observable behavior (queue depths, retry counts)
- Rich failure scenarios (timeouts, rate limits, provider errors)
- Idempotency verification

**S4.DevHub** - 🟩 Good security testing focus
- Secret redaction validation
- Environment isolation testing
- Rotation zero-downtime verification
- Audit trail completeness

**S6.MediaHub** - Validation and pipeline testing critical
- File validation testing
- Processing pipeline stages
- Storage tier verification

**S9.OrderFlow** - 🟦 Excellent deterministic testing
- Event replay determinism
- Projection consistency
- Compensation transaction testing
- Time-travel verification

### Enterprise Architect Evaluation Summary

**Interest & Accessibility Matrix:**

| Sample | Business Interest | Technical Interest | Accessibility | Enterprise Readiness |
|--------|------------------|-------------------|---------------|---------------------|
| **S3.NotifyHub** | 🟦 Very High | 🟩 High | 🟦 Easy | 🟩 Good |
| **S4.DevHub** | 🟦 Critical | 🟩 High | 🟨 Moderate | 🟦 Excellent |
| **S6.MediaHub** | 🟩 High | 🟩 High | 🟩 Good | 🟩 Good |
| **S9.OrderFlow** | 🟩 High | 🟦 Very High | 🟧 Moderate-Low | 🟩 Good |

**Target Audience Alignment:**

- **S3.NotifyHub**: SaaS developers, product teams, anyone learning async patterns
- **S4.DevHub**: Security/compliance teams, DevOps, platform engineering
- **S6.MediaHub**: Teams building media-centric applications
- **S9.OrderFlow**: Architects evaluating event sourcing, e-commerce/fintech domains

### Capability Coverage Improvements

**Before Realignment: 58% coverage (14/24 major capabilities)**

**After Realignment: 92% coverage (22/24 major capabilities)**

Remaining gaps (acceptable for v0.6.x):
- GraphQL endpoints (niche, can add to existing sample)
- Observability recipe system (future dedicated sample)

## Consequences

### Positive

**Developer Experience:**
- ✅ Clear learning path from beginner to advanced
- ✅ "Which sample shows X?" answered by capability matrix
- ✅ Consistent documentation quality across all samples
- ✅ Compelling domain applications, not framework demos

**Framework Adoption:**
- ✅ Fills critical enterprise gaps (secrets, CQRS/outbox)
- ✅ Demonstrates real-world patterns (notifications, order management)
- ✅ Reduces "does Koan support X?" questions
- ✅ Provides objective performance data (S14.AdapterBench)

**Quality & Maintenance:**
- ✅ Reduced from 19 to 14 samples (26% reduction)
- ✅ Every sample has comprehensive README and tests
- ✅ Consistent project structure reduces cognitive load
- ✅ Testing patterns demonstrated across collection

**Coverage & Completeness:**
- ✅ 92% capability coverage (up from 58%)
- ✅ All 8 architectural layers represented
- ✅ Enterprise patterns (event sourcing, secret management) shown

### Negative

**Short-term Effort:**
- ⚠️ 16-20 weeks of development for 4 new samples (50-80 days total)
- ⚠️ Documentation standardization across existing samples (2-3 weeks)
- ⚠️ Sample catalog and capability matrix creation (1 week)

**Archival Risks:**
- ⚠️ Users relying on archived samples need migration guidance
- ⚠️ External references to archived samples (blogs, tutorials) will break
- ⚠️ Historical context lost if not properly documented before archival

**Complexity Introduction:**
- ⚠️ S9.OrderFlow advanced pattern may overwhelm some developers
- ⚠️ S4.DevHub Vault setup has operational complexity
- ⚠️ New samples require more infrastructure (RabbitMQ, Vault, etc.)

### Mitigations

**Archival Process:**
1. Move archived samples to `samples/archive/` directory (not deleted)
2. Add `ARCHIVED.md` explaining why and pointing to replacements
3. Update any external documentation with migration notes
4. Keep git history intact for reference

**Complexity Management:**
1. **S4.DevHub**: "Easy Start" mode using only appsettings (no Vault required)
2. **S9.OrderFlow**: Clearly label as "Advanced" with "When NOT to use" section
3. All samples: Progressive disclosure - start simple, add complexity incrementally

**Development Efficiency:**
1. Prioritize by impact: S3 and S4 first (fill critical gaps)
2. Reuse patterns from existing samples (S5.Recs UI patterns)
3. Parallel development where possible (S3 and S4 can run simultaneously)

## Implementation Plan

### Phase 1: Cleanup & Foundation (Weeks 1-2)

**Week 1:**
- ✅ Archive samples: Move S2, S4.Web, S6.Auth, S6.SocialCreator, S12, S15, KoanAspireIntegration to `samples/archive/`
- ✅ Create `ARCHIVED.md` files with migration guidance
- ✅ Update port allocation registry (OPS-0014)

**Week 2:**
- ✅ Merge S7.TechDocs + S7.ContentPlatform → S7.DocPlatform
- ✅ Create sample catalog README with capability matrix
- ✅ Update root samples/README.md with new structure

### Phase 2: S3.NotifyHub (Weeks 3-6)

**Week 3-4: Core Implementation**
- Basic notification API (email, SMS stubs)
- RabbitMQ integration for queueing
- Simple retry logic
- Entity models (NotificationMessage, DeliveryAttempt)
- Basic seeding

**Week 5: Patterns**
- Redis Inbox for idempotency
- Koan.Scheduling for scheduled delivery
- Dead letter queue handling
- Batch campaign support

**Week 6: Polish**
- Comprehensive README (S5.Recs template)
- Dashboard UI (queue status, delivery tracking)
- Testing examples (retry, idempotency, batch)
- Provider mock implementations

### Phase 3: S4.DevHub (Weeks 7-10)

**Week 7-8: Local Development**
- Service integration models (GitHub, Slack, etc.)
- Environment configuration system
- Secret resolution from appsettings
- Health checks for services
- Dashboard UI (service status cards)

**Week 9: Vault Integration**
- Koan.Secrets.Connector.Vault integration
- Secret rotation demonstration
- Audit log implementation
- Migration guide (appsettings → Vault)

**Week 10: Polish**
- Comprehensive README with progressive disclosure
- "Easy Start" guide (no Vault)
- "Production Ready" guide (Vault)
- Testing examples
- Mock Vault for development

### Phase 4: S6.MediaHub (Weeks 11-15)

**Week 11-12: Core Pipeline**
- Upload validation
- Image resizing service
- Storage profile system
- Entity models (MediaAsset, ProcessingJob)

**Week 13-14: Storage & Backup**
- Koan.Storage integration
- Hot/cold storage tiering
- Koan.Data.Backup demonstration
- Batch processing

**Week 15: Polish**
- Comprehensive README
- UI for upload and management
- Testing examples
- Performance considerations

### Phase 5: S9.OrderFlow (Weeks 16-21)

**Week 16-17: Event Store Foundation**
- Order aggregate with business logic
- Event store (append-only)
- Events: OrderPlaced, PaymentReceived, OrderShipped
- Event replay mechanism

**Week 18-19: CQRS Projections**
- Customer view projection
- Support view projection
- Analytics projection
- Projection rebuild from events

**Week 20: Outbox & Messaging**
- Koan.Data.Cqrs.Outbox implementation
- Email confirmations on state changes
- Webhook notifications
- Integration with messaging layer

**Week 21: Advanced & Polish**
- Compensating transactions (refunds)
- Snapshot system for performance
- Event versioning demonstration
- Comprehensive README with trade-offs
- "When NOT to use event sourcing" section
- Testing examples

### Phase 6: Documentation Standardization (Weeks 22-24)

**Parallel to Phase 2-5:**
- Apply S5.Recs README template to existing samples
- Add test examples to samples lacking them
- Create cross-reference links between samples
- Update capability matrix as new samples complete

**Week 22-24:**
- Final documentation pass
- Screenshot updates
- Video walkthroughs (optional)
- External documentation updates

## Metrics & Success Criteria

### Coverage Goals
- ✅ Target: 90%+ framework capability coverage
- ✅ Expected: 92% (22/24 major capabilities)
- ✅ All 8 architectural layers represented

### Quality Goals
- ✅ Every sample has comprehensive README (S5.Recs template)
- ✅ Every sample includes test examples
- ✅ Consistent project structure across all samples
- ✅ "Which sample shows X?" answered in <5 seconds via capability matrix

### Developer Experience Goals
- ✅ Clear beginner → intermediate → advanced path
- ✅ Samples demonstrate 2-3 capabilities together (real-world patterns)
- ✅ Compelling domain applications, not framework showcases
- ✅ One-command start for all samples

### Validation Plan
- User feedback from first 10 developers using new samples
- Time-to-productivity measurement (S0 → production-ready app)
- "Which sample?" question frequency reduction in community channels
- Framework evaluation conversion rate (evaluators → adopters)

## Follow-ups

### Immediate (Weeks 1-2)
1. Execute Phase 1: Archival and cleanup
2. Create migration guide for archived samples
3. Update port allocation registry
4. Create sample catalog README

### Short-term (Weeks 3-21)
1. Build S3.NotifyHub, S4.DevHub, S6.MediaHub, S9.OrderFlow
2. Standardize documentation across existing samples
3. Add test examples to samples lacking them

### Medium-term (Post-v0.6.3)
1. **S2.WebApi** - Intermediate learning sample (bridge S1 → S5)
2. **S11.Observability** - Recipe system and monitoring patterns
3. **S13.Polyglot** - Enterprise multi-provider showcase
4. **S17.TestPatterns** - Comprehensive testing guide
5. **S18.Dapr** - Dapr integration (if demand exists)

### Long-term
1. Video walkthroughs for each sample
2. Interactive tutorials (CodeSandbox/GitHub Codespaces)
3. Sample performance regression testing in CI/CD
4. Community-contributed samples program

## References

### Internal Documentation
- `docs/architecture/capability-map.md` - Framework capability layers
- `docs/architecture/module-ledger.md` - Module dependency inventory
- `docs/architecture/comparison.md` - Framework differentials vs. competitors
- `docs/decisions/DX-0044-adapter-benchmark-sample.md` - S14.AdapterBench ADR
- `docs/decisions/OPS-0014-samples-port-allocation.md` - Port allocation standards

### Related Decisions
- **DX-0044**: Adapter benchmark sample (S14.AdapterBench architecture)
- **DX-0042**: Narrative samples entity naming conventions
- **DATA-0061**: Data access pagination and streaming patterns
- **WEB-0035**: EntityController transformer patterns
- **ARCH-0058**: Canon runtime architecture (S8.Canon)
- **AI-0014**: MCP Code Mode (S16.PantryPal)

### Sample Cross-References
- **S5.Recs** - Template for README structure and tutorial quality
- **S10.DevPortal** - Multi-provider demonstration patterns
- **S14.AdapterBench** - Performance benchmarking approach
- **S16.PantryPal** - AI integration and testing patterns
- **guides/g1c1.GardenCoop** - Guide narrative structure

## Notes

### Design Philosophy

This realignment follows core Koan principles:

1. **"Reference = Intent"** - Samples should show that adding a package reference automatically enables functionality
2. **Entity-First Development** - All samples use Entity<T> patterns, not manual repositories
3. **Multi-Provider Transparency** - Show same code working across different storage backends
4. **Real-World Patterns** - Domain applications that naturally require framework capabilities
5. **Progressive Complexity** - Start simple, add sophistication incrementally

### Sample Naming Convention

Format: `SX.AppName` where:
- `X` = Sequential number (0-18+)
- `AppName` = PascalCase application name (not "Sample" or framework feature name)
- Examples: S3.NotifyHub, S4.DevHub, S5.Recs, S9.OrderFlow

Avoid:
- ❌ S3.MessagingSample (too generic)
- ❌ S4.SecretsDemo (framework-focused, not domain-focused)
- ❌ S9.EventSourcingSample (pattern name, not application name)

### Community Engagement

This ADR should be shared with:
- Early adopters for feedback on sample prioritization
- Enterprise evaluators for validation of gap coverage
- Framework contributors for implementation coordination
- Documentation team for README template standardization

---

**Last Updated**: 2025-10-16
**Next Review**: Post-Phase 1 completion (Week 3)
**Status**: Proposed - Awaiting approval for Phase 1 execution
