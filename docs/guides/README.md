# Koan Framework Guides

**Comprehensive documentation for developers working with the Koan Framework.**

---

## 🚨 Troubleshooting

**Start here when things aren't working properly.**

### [Adapter Connection Issues](troubleshooting/adapter-connection-issues.md)
- Database connectivity failures
- Schema auto-provisioning problems
- SDK bootstrap and timing issues
- Service readiness verification

*Common symptoms*: `SocketNotAvailableException`, `Service n1ql not configured`, empty collections returning `[]`

### [Bootstrap Failures](troubleshooting/bootstrap-failures.md)
- Application startup problems
- Reference data not seeding
- Startup task discovery issues
- Bootstrap timing coordination

*Common symptoms*: Missing MediaTypes, "Please seed reference data first" errors, empty reference endpoints

---

## 🔬 Deep Dive

**Understanding how the framework works internally.**

### [Auto-Provisioning System](deep-dive/auto-provisioning-system.md)
- `AdapterReadinessExtensions` architecture
- `IInstructionExecutor<T>` pattern
- Schema failure detection and recovery
- Multi-provider transparency implementation

*For developers*: Understanding Entity<> "just works" behavior

### [Bootstrap Lifecycle](deep-dive/bootstrap-lifecycle.md)
- Multi-layer initialization coordination
- Infrastructure → Framework → Application startup
- Startup task discovery and execution
- Timing dependencies and coordination

*For contributors*: How application initialization actually works

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

### [AI Integration](ai-integration.md)
- Vector stores and semantic search
- AI service integration patterns
- Embedding and retrieval workflows

### [Authentication Setup](authentication-setup.md)
- Zero-config OAuth and JWT
- Service-to-service authentication
- Custom auth provider integration

### [Semantic Pipelines](semantic-pipelines.md)
- Streaming data processing with AI enrichments
- Background orchestration patterns
- Observability guidance for long-running jobs

### [Performance Optimization](performance.md)
- Query tuning across providers
- Background worker throughput strategies
- Diagnostics and tracing recommendations

### [Expose MCP over HTTP + SSE](mcp-http-sse-howto.md)
- Configure Koan's MCP host for remote IDEs
- SSE transport considerations
- Agent onboarding checklist

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