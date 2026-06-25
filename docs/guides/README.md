---
type: GUIDE
domain: framework
title: "Guides Index"
audience: [developers, architects]
status: current
last_updated: 2025-10-09
framework_version: v0.6.3
validation:
	status: not-yet-tested
	scope: docs/guides/README.md
---

# Koan Framework Guides

**Comprehensive documentation for developers working with the Koan Framework.**

---

## 🚨 Troubleshooting

**Start here when things aren't working properly.**

### [Troubleshooting Hub](../support/troubleshooting.md)

- Adapter and data connectivity checks
- Boot and auto-registration diagnostics
- AI, Flow, and health endpoint runbooks
- Escalation template with required artifacts

_Highlights_: Stage backlog queries, provider readiness probes, AI rate-limit tuning, and bootstrap task patterns.

---

## 🔬 Deep Dive

**Understanding how the framework works internally.**

### [Auto-Provisioning System](deep-dive/auto-provisioning-system.md)

- `AdapterReadinessExtensions` architecture
- `IInstructionExecutor<T>` pattern
- Schema failure detection and recovery
- Multi-provider transparency implementation

_For developers_: Understanding Entity<> "just works" behavior

### [Bootstrap Lifecycle](deep-dive/bootstrap-lifecycle.md)

- Multi-layer initialization coordination
- Infrastructure → Framework → Application startup
- Startup task discovery and execution
- Timing dependencies and coordination

_For contributors_: How application initialization actually works

---

## 📖 Developer Guides

**Step-by-step guides for building applications.**

### [Building APIs](building-apis.md)

- REST APIs with zero configuration
- Entity controller patterns powered by `EntityController<T>`
- Custom endpoint extensions and transformers

### [Data Modeling](data-modeling.md)

- Entity-first development patterns
- Multi-provider storage design
- Relationship and navigation patterns

### [Background Jobs](jobs-howto.md)

- Entity-first jobs: one `Execute` method, no queues or workers to wire
- Chains, per-action policy (retry/timeout/lanes), idempotency, and cooperative backoff
- Scheduling (reconcile loops), durability tiers, and distributed claim strategies

### [Media Recipes](media-recipes-howto.md)

- Format-preserving image pipeline (animated WebP, transparent PNG round-trip)
- Recipe registry: `[MediaRecipe]` attribute + `Koan:Media:Recipes` config
- HTTP grammar with named recipes, format shortcuts, and mutator allowlists
- Multi-variant materialisation (one decode, N outputs)

### [Multi-Tenancy](tenancy-howto.md)

- Reference = Intent: every non-`[HostScoped]` `Entity<T>` is isolated by the ambient tenant — no tenant column, no `WHERE` filter
- Automatic isolation across data reads/writes, blob storage (STOR-0011), cache, and the job async-hop
- `Tenant.Use`/`None`/`Current` scoping, dev-open / prod-closed posture, and the `DataAxis.AssertNoLeak` proof
- Retrofitting tenancy onto an app: `[HostScoped]` global rows, per-request middleware, background-worker scoping

### [AI Integration](ai-integration.md)

- Vector stores and semantic search
- AI service integration patterns
- Embedding and retrieval workflows

### [Authentication & Identity](auth-howto.md)

- Zero-config dev identity → roles → real logins → service tokens (KSVID) → production
- `Identity.Current`, persona testing (`?_as=`), and the fail-closed production posture
- Provider/OAuth/SAML configuration reference: [Authentication Setup](authentication-setup.md)
- Issuing tokens (the MCP auth on-ramp): [Embedded OAuth 2.1 Authorization Server](oauth-server-howto.md)

### [Authorization](authorization-howto.md)

- The `IAuthorize` seam and capability-graded provider ladder
- Capability gates (`[RequireCapability]`), named policies, and custom PDP/ReBAC providers
- "Coarse in the token, fine at the resource"

### [AI & Vector Search](ai-vector-howto.md)

- Streaming data processing with AI enrichments (`AllStream` → `Tokenize` → `SaveWithVectors`)
- Batch-processing pipeline patterns and branching
- Observability guidance for long-running enrichment jobs

### [Performance Optimization](performance.md)

- Query tuning across providers
- Background worker throughput strategies
- Diagnostics and tracing recommendations

### [Publishing with NativeAOT](nativeaot-howto.md)

- The `KoanAot` opt-in, what the framework roots for you, Windows/Linux prerequisites
- The Newtonsoft / Dapper-free / no-`dynamic` constraints, plus troubleshooting
- Edge-case mitigations for SQLite, globalization, and VC++ toolchain gaps

### [Expose MCP over HTTP + SSE](mcp-http-sse-howto.md)

- Configure Koan's MCP host for remote IDEs
- SSE transport considerations
- Agent onboarding checklist

---

## 🧪 Pattern Catalog

**Scenario-driven walkthroughs that connect multiple pillars.**

### [Entity Pattern Recipe Catalog](../examples/entity-pattern-recipes.md)

- CRUD, messaging, and AI recipes built on a single entity model
- Cross-pillar checklists for Web, Flow, AI, and Messaging
- Links back to the Data Modeling Playbook for deeper coverage

### [Garden Cooperative Journal](garden-cooperative-journal.md)

- Narrative-bound slice showing Plot, Reading, Reminder, and Member working together
- SQLite-first setup with Flow hydration scoring and lifecycle events
- API storyboard with minimal controllers and optional reminder extensions
- Proposal spec: [Garden Cooperative Journal How-To Spec](../archive/proposals/complete/garden-cooperative-journal.md)

---

## 📊 Documentation Priorities

### Immediate Needs (Implemented)

- ✅ **Troubleshooting guides** - Resolving common production issues
- ✅ **Deep-dive documentation** - Understanding complex systems

### High Priority (Next Phase)

- 🔄 **Operations guides** - Production deployment and monitoring
- 🔄 **Developer experience** - Onboarding and productivity guides
- 🔄 **Performance optimization** - Tuning and scaling patterns

### Future Enhancements

- 🗓️ **Video tutorials** - Visual learning for complex topics
- 🗓️ **Interactive examples** - Live code samples and demos
- 🗓️ **Community contributions** - User-generated guides and patterns

---

## 🎯 Getting Help

### For Troubleshooting Issues

1. **Start with troubleshooting guides** - Most common issues are covered
2. **Check deep-dive docs** - Understand the underlying systems
3. **Search existing issues** - Problem may already be documented
4. **Create issue with details** - Provide logs and reproduction steps

### For Development Questions

1. **Review architecture principles** - Understand framework design philosophy
2. **Study code examples** - Learn from working implementations
3. **Join community discussions** - Connect with other developers
4. **Contribute documentation** - Help others learn from your experience

### For Framework Contributors

1. **Read deep-dive documentation** - Understand internal architecture
2. **Review troubleshooting patterns** - Learn common failure modes
3. **Study testing approaches** - Follow established testing patterns
4. **Document new features** - Maintain high documentation standards

---

**The goal of this documentation is to transform complex framework internals into clear, actionable knowledge that enables developers to build sophisticated applications with confidence.**
