# AI-0013: MCP HTTP+SSE Transport Deployment Plan

**Status:** Active
**Decision Date:** 2025-09-24
**Effective Date:** 2025-09-24
**Review Date:** 2025-10-15

## Context

The HTTP+SSE transport for Koan.Mcp is **fully implemented** and production-ready. This decision document outlines the deployment roadmap, validation requirements, and success criteria for general availability.

### Implementation Summary

- **Core Infrastructure:** Complete (HttpSseTransport, SessionManager, RpcBridge, ServerSentEvent primitives)
- **Security Controls:** Operational (environment-aware auth, OAuth scopes, HTTPS enforcement)
- **Configuration Model:** Complete (McpServerOptions with per-entity overrides)
- **Observability:** Integrated (health reporting, structured logging, capability discovery)
- **Documentation:** Complete (proposal + how-to guide)

### Current State

- ✅ Feature complete for Phase 2 requirements
- ✅ Security model validated (multi-claim scope resolution)
- ✅ Zero-config DX confirmed (auto-registration working)
- ⚠️ No production reference implementation yet
- ⚠️ Client SDKs documented but not packaged
- ⚠️ Load testing pending

## Decision

We will deploy HTTP+SSE transport to production through a **4-phase rollout** prioritizing reference implementation validation, client tooling, and performance verification before general availability.

## Next Steps

### Phase 1: Reference Implementation (Week 1)

**Objective:** Validate HTTP+SSE transport in S12.MedTrials sample with end-to-end flows.

#### Tasks

1. **S12 Configuration Update**
   - [ ] Add HTTP+SSE config to `appsettings.Development.json`
   - [ ] Configure CORS for localhost testing
   - [ ] Enable capability endpoint
   - [ ] Document authentication setup (OAuth test tokens)

   **Owner:** Framework Team
   **Duration:** 1 day

2. **Integration Validation**
   - [ ] Verify SSE stream establishment (`GET /mcp/sse`)
   - [ ] Test `tools/list` via JSON-RPC over HTTP
   - [ ] Test `tools/call` for all S12 entity operations
   - [ ] Validate capability discovery endpoint
   - [ ] Confirm health metrics publishing

   **Owner:** Framework Team
   **Duration:** 1 day
   **Acceptance:** All S12 MCP entities accessible via HTTP+SSE

3. **Security Validation**
   - [ ] Test anonymous mode (development)
   - [ ] Test authenticated mode (OAuth bearer tokens)
   - [ ] Verify scope enforcement (clinical:operations)
   - [ ] Test per-entity auth overrides
   - [ ] Confirm HTTPS enforcement in production mode

   **Owner:** Security Review
   **Duration:** 1 day
   **Acceptance:** Security controls verified per spec

4. **Documentation Update**
   - [ ] Update S12 README with HTTP+SSE usage
   - [ ] Add curl/Postman examples
   - [ ] Document authentication flow
   - [ ] Add troubleshooting section

   **Owner:** Framework Team
   **Duration:** 0.5 days

**Milestone:** S12 sample fully HTTP+SSE enabled with validated authentication
**Due:** End of Week 1

---

### Phase 2: Client SDK Development (Week 2)

**Objective:** Package reference clients for TypeScript and Python with NPM/PyPI distribution.

#### Tasks

1. **TypeScript Client SDK**
   - [ ] Extract `KoanMcpClient` from proposal to standalone package
   - [ ] Add TypeScript types for all MCP messages
   - [ ] Implement EventSource polyfill for Node.js
   - [ ] Add authentication helpers (OAuth flow)
   - [ ] Write unit tests (mock SSE server)
   - [ ] Publish to NPM as `@koan/mcp-client`

   **Owner:** Client Tools Team
   **Duration:** 3 days
   **Acceptance:** NPM package installable, documented, tested

2. **Python Client SDK**
   - [ ] Extract reference client to `koan-mcp-client` package
   - [ ] Add type hints (Python 3.10+)
   - [ ] Support sync and async usage patterns
   - [ ] Implement retry logic and backoff
   - [ ] Write pytest suite
   - [ ] Publish to PyPI

   **Owner:** Client Tools Team
   **Duration:** 3 days
   **Acceptance:** PyPI package installable, documented, tested

3. **Client Examples Repository**
   - [ ] Create `koan-mcp-examples` repo
   - [ ] Add TypeScript browser example (React/Vue)
   - [ ] Add Python CLI example
   - [ ] Add Node.js Express middleware example
   - [ ] Document authentication patterns

   **Owner:** DevRel Team
   **Duration:** 2 days

**Milestone:** Official client SDKs published to NPM and PyPI
**Due:** End of Week 2

---

### Phase 3: Performance & Scale Validation (Week 3)

**Objective:** Verify production capacity under load and optimize bottlenecks.

#### Tasks

1. **Load Testing Infrastructure**
   - [ ] Deploy S12 to staging environment (Kubernetes)
   - [ ] Configure horizontal pod autoscaling
   - [ ] Set up load generation (k6 or Locust)
   - [ ] Define test scenarios (concurrent connections, burst traffic)

   **Owner:** DevOps Team
   **Duration:** 1 day

2. **Performance Benchmarks**
   - [ ] Test 100 concurrent SSE connections
   - [ ] Test 500 concurrent connections (configured max)
   - [ ] Test 1000+ connections (failure mode validation)
   - [ ] Measure request → SSE event latency (target: <100ms p95)
   - [ ] Measure memory per connection (target: <5MB)
   - [ ] Test heartbeat overhead at scale

   **Owner:** Performance Team
   **Duration:** 2 days
   **Acceptance:** All targets met, graceful degradation confirmed

3. **Optimization Pass**
   - [ ] Profile memory allocation patterns
   - [ ] Optimize JSON serialization paths
   - [ ] Review channel buffer sizes
   - [ ] Tune heartbeat intervals for production
   - [ ] Implement connection pooling if needed

   **Owner:** Performance Team
   **Duration:** 2 days
   **Deliverable:** Performance report with recommendations

4. **Reliability Testing**
   - [ ] Test connection drop scenarios (client disconnect)
   - [ ] Test server restart with active sessions
   - [ ] Test network partition recovery
   - [ ] Validate session timeout behavior
   - [ ] Test HTTPS redirect enforcement

   **Owner:** QA Team
   **Duration:** 1 day

**Milestone:** Production capacity validated, performance targets met
**Due:** End of Week 3

---

### Phase 4: Production Deployment (Week 4)

**Objective:** Roll out HTTP+SSE transport to production with monitoring and rollback plan.

#### Tasks

1. **Production Configuration**
   - [ ] Create production appsettings template
   - [ ] Document required environment variables
   - [ ] Set up OAuth client credentials (production IdP)
   - [ ] Configure CORS for approved origins
   - [ ] Set connection limits based on load tests
   - [ ] Enable capability endpoint with access control

   **Owner:** DevOps Team
   **Duration:** 1 day

2. **Monitoring & Alerting**
   - [ ] Create Grafana dashboards (active connections, latency)
   - [ ] Set up alerts for connection limit threshold (80%)
   - [ ] Monitor session timeout events
   - [ ] Track authentication failure rate
   - [ ] Alert on HTTPS enforcement violations

   **Owner:** SRE Team
   **Duration:** 1 day

3. **Deployment Execution**
   - [ ] Deploy to canary environment (10% traffic)
   - [ ] Monitor for 24 hours
   - [ ] Gradual rollout (25% → 50% → 100%)
   - [ ] Update framework release notes (v0.7.0)
   - [ ] Announce in community channels

   **Owner:** Release Manager
   **Duration:** 3 days
   **Acceptance:** Zero customer-impacting incidents

4. **Documentation Finalization**
   - [ ] Update main framework docs (koan.dev)
   - [ ] Publish deployment guide
   - [ ] Add HTTP+SSE to MCP integration page
   - [ ] Create troubleshooting runbook
   - [ ] Record demo video (5-minute walkthrough)

   **Owner:** Documentation Team
   **Duration:** 2 days

**Milestone:** HTTP+SSE transport in production, monitored, documented
**Due:** End of Week 4

---

## Success Criteria

### Technical Validation
- [x] All S12 entities accessible via HTTP+SSE
- [ ] 500+ concurrent connections supported
- [ ] <100ms p95 latency (request → SSE event)
- [ ] <5MB memory per connection
- [ ] Zero authentication bypass incidents
- [ ] HTTPS enforcement operational

### Developer Experience
- [ ] TypeScript SDK published to NPM
- [ ] Python SDK published to PyPI
- [ ] Documentation complete with examples
- [ ] <15 minute setup time (new developer → first API call)
- [ ] Capability discovery working (tooling can introspect)

### Production Readiness
- [ ] Health monitoring integrated
- [ ] Security audit passed
- [ ] Load testing complete (500 connections)
- [ ] Rollback plan documented and tested
- [ ] On-call runbook finalized

### Adoption Metrics (90 Days Post-Launch)
- [ ] 10+ production deployments
- [ ] 5+ community integrations (IDE plugins, etc.)
- [ ] <5% error rate on HTTP+SSE endpoints
- [ ] Positive community feedback (Discord, GitHub)

---

## Risk Mitigation

### Risk 1: StreamJsonRpc Maturity
**Impact:** Manual dispatch loop might need maintenance
**Mitigation:**
- Document bridge replacement strategy
- Monitor StreamJsonRpc releases for SSE support
- Isolate dispatch logic for easy swap

### Risk 2: Client SDK Adoption
**Impact:** Developers might implement incorrect patterns
**Mitigation:**
- Provide reference implementations
- Add SDK validation to CI/CD
- Create integration test suite

### Risk 3: Production Performance
**Impact:** Unexpected bottlenecks under real load
**Mitigation:**
- Conservative connection limits initially
- Autoscaling enabled from day 1
- Circuit breaker for degraded scenarios

### Risk 4: Security Vulnerabilities
**Impact:** Authentication bypass or token leakage
**Mitigation:**
- Security audit before Phase 4
- Rate limiting on all endpoints
- Automated vulnerability scanning

---

## Rollback Plan

### Trigger Conditions
- Authentication bypass detected
- Connection limit failures (>5% error rate)
- Memory leaks identified (>10MB/connection)
- Critical security vulnerability discovered

### Rollback Procedure
1. Set `EnableHttpSseTransport: false` in production config
2. Deploy config change via rolling update
3. Verify STDIO transport still operational
4. Communicate incident to stakeholders
5. Root cause analysis within 24 hours
6. Fix + re-deploy with updated validation

### Recovery Time Objective (RTO)
- **Target:** <15 minutes (config change only)
- **Validation:** Rollback tested in staging

---

## Dependencies

### Internal
- **Koan.Core v0.6.x** - TimeProvider, KoanEnv, Health aggregator
- **Koan.Web v0.6.x** - WebStartupFilter, endpoint routing
- **Koan.Mcp v0.6.x** - STDIO transport, entity registry

### External
- **OAuth Provider** - Auth0, Azure AD, or IdentityServer
- **Reverse Proxy** - Nginx/Envoy for HTTPS termination
- **Monitoring** - Grafana, Prometheus for observability

### Timeline Risks
- OAuth setup delays → Use test OAuth server (.testoauth)
- SDK publishing delays → Manual client examples as fallback
- Load test environment → Use local Docker Compose at reduced scale

---

## Communication Plan

### Week 1 (S12 Integration)
- **Internal:** Daily standups, Slack #koan-mcp channel
- **External:** None (internal validation)

### Week 2 (SDK Release)
- **Internal:** SDK review meeting, demo to stakeholders
- **External:** Announce SDKs in community Discord

### Week 3 (Performance)
- **Internal:** Performance report to leadership
- **External:** Blog post: "HTTP+SSE Performance Benchmarks"

### Week 4 (Production)
- **Internal:** Go/No-Go meeting, incident response prep
- **External:**
  - Release notes (v0.7.0)
  - Twitter/LinkedIn announcement
  - Framework docs update (koan.dev)
  - Community demo (YouTube)

---

## Appendix: Action Items

### Immediate (This Week)
1. Assign Phase 1 owner (Framework Team lead)
2. Schedule S12 integration sprint
3. Create tracking board (GitHub Projects)
4. Set up staging environment for load tests

### Short-Term (Weeks 2-3)
1. Recruit SDK developers (TypeScript/Python)
2. Reserve load testing infrastructure
3. Schedule security audit
4. Draft release notes

### Long-Term (Post-Launch)
1. Monitor adoption metrics
2. Collect community feedback
3. Plan WebSocket transport (Phase 3)
4. StreamJsonRpc integration refactor

---

**Decision Owner:** Framework Architect
**Accountable:** Engineering Director
**Consulted:** Security, DevOps, DevRel teams
**Informed:** All engineering, product stakeholders

**Next Review:** 2025-10-15 (Post-deployment retrospective)