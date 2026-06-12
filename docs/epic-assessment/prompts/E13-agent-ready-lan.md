# E13 — The Agent-Ready LAN (koi-mcp + the MCP Layering, Composed)

**Repo(s)**: Koi (build) + Koan (integration) · **Phase**: E · **Prereqs**: E05 (Koi works
for programs), E12 (envelope as MCP resource); Koan-side governance deepens when KOAN
07-card P3.1 (grants) lands — do not block on it · **One to two sessions** ·
Read [CHARTER.md](CHARTER.md) in full first.

## Mission

Ship the stack's highest-leverage agent capability: **an agent on the LAN can discover a
named, TLS-trusted tool surface — and operate a Koan application through it.** Koi answers
*where* (LAN MCP discovery is unserved; the community port-scans localhost); Koan answers
*who/how much* (entity tools + governance). The composed demo at the end is the market
claim nobody else can make.

## Context (verify each)

- **Koi already has a full build card for the MCP server**: `KOI/docs/prompts/P11-koi-mcp.md`
  (`koi mcp serve` stdio server via the rmcp SDK; tools incl. `mcp_servers_on_lan`
  discovering `_mcp._tcp` + `_mcp._sub`; self-advertisement as `_koi-mcp._tcp`). **Execute
  P11 as written** under Koi's own charter — this prompt adds only the Epic layering
  constraints and the Koan composition below.
- **Koan's MCP surface is shipped**: endpoints auto-map via `McpEndpointContributor`
  (`KOAN/src/Koan.Mcp/...`); `[McpEntity]` controls exposure (RequiredScopes,
  AllowMutations, transports).
- The layering doctrine (STACK-0001 item 5): Koi = network-substrate MCP + discovery OF MCP
  servers; Koan = application MCP. **Koi advertises Koan endpoints; it never wraps them.**

## DECIDED

1. **Koi side**: P11 as specced, plus: the MCP tool list stays within the contract surface
   (no proxy tools — STACK-0001 item 7); the E12 self-description document is exposed as an
   MCP resource; tool descriptions follow the enabler doctrine (they help agents *find and
   trust* services, not replace the services' own tools).
2. **Advertisement seam**: a Koan app's MCP endpoint becomes discoverable by registering an
   `_mcp._tcp` service through Koi's existing register API. Implementation lands in the
   Koan ZenGarden/Koi satellite (NOT mainline): when the satellite is referenced and a Koi
   daemon is reachable, the app announces its MCP endpoint (name, port, path,
   TLS-expected) on start and deregisters on stop — Koi's lease semantics handle crashes.
   Autonomous fallback: no Koi present → no announcement, no error (works alone).
3. **Trust posture in the demo**: the announced endpoint serves HTTPS from a certmesh cert
   (E10's Kestrel helper) so the agent's connection is trusted via the pond CA / OS store.
4. **Governance posture, stated honestly**: until Koan's grants card lands, the demo uses
   `[McpEntity]` scoping/read-only-by-default as shipped; the demo doc states governance
   maturity plainly and links the Koan card as the upgrade path.
5. **The composed demo** (scripted in `EPIC/demo/agent-lan.md`): start Koi + a Koan sample
   with the satellite → from a second machine (or clean shell), run `koi mcp serve`-backed
   discovery (`mcp_servers_on_lan`) → the Koan app appears by name → connect with any
   MCP-capable agent client → list tools → perform one read and one (allowed) mutation →
   show the cert chain validating against the pond CA.

## DEFAULT

- Announcement details (instance naming, TXT records for path/transport): follow whatever
  `_mcp._tcp` conventions exist in the wild as of execution time (research step — check the
  MCP spec/registry discussions); record the chosen TXT schema in the satellite README so
  it can be revised when a convention standardizes. Support is additive, not a bet.

## Plan of record

1. Execute KOI P11 (its own DoD applies). 2. Build the satellite announcement seam +
fallback test. 3. Wire E12's resource into koi-mcp. 4. Script + run the composed demo;
capture transcripts. 5. Guards: koi-mcp tool tests (P11's), satellite announce/deregister
integration test against a real Koi. 6. SURFACES.md (Koi: mcp row; Koan: satellite row).
7. Docs: satellite README + demo doc with the honest-governance note.

## Verification

The composed demo run end to end, captured: discovery returns the Koan app by name; tools
list matches `[McpEntity]` exposure; TLS chain validates; deregistration on app stop
(observe the lease expire/DRAINING per Koi's lifecycle).

## Definition of done

- [ ] koi-mcp shipped per P11; layering constraints held (no proxy tools, no wrapping Koan).
- [ ] Satellite announces/deregisters with autonomous fallback; tests prove both modes.
- [ ] Composed demo scripted + captured; governance posture stated honestly.
- [ ] SURFACES.md updated in both repos.
