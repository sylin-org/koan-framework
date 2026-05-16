# C1 — Microsoft Agent Framework (optional kit)

**Intent**: Provide an optional `Koan.AI.Agents` package that wires Microsoft **Agent Framework** over Koan’s Data/Vector and MCP libraries to rapidly compose multi‑agent workflows. citeturn8search0turn8search5

## Plan
1) Package `Koan.AI.Agents` with an auto-registrar that exposes:
   - A `KoanAgent` base wired to Koan Data/Vector search and `Microsoft.Extensions.AI` chat client. citeturn5search0
   - Turnkey **MCP** tool bridges using the official **MCP C# SDK**. citeturn8search1
2) Samples:
   - A retrieval‑augmented agent using Weaviate/PGVector + SSE streaming (A2).  
   - An MCP tool‑calling agent scenario.

## Guardrails
- Keep this optional to avoid spreading Koan thin.  
- Prefer simple defaults, low ceremony.  
- Verify licensing/compliance notes from the project. citeturn8search2

## Acceptance Criteria
- Demo runs an agent with SSE streaming and MCP tool calls.  
- Clear docs show when to use Agents vs simple workflows.
