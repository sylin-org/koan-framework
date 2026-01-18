# B1 — Source-Generated Registries (AOT-friendly)

**Intent**: Replace reflection-heavy module/provider scanning with a **source generator** that emits a static registry map at build time (preserving current DI contracts).  
**Why**: Faster cold start, better **Native AOT** compatibility, and deterministic boot reports. fileciteturn0file16

## Plan
1) Implement `Koan.Generators.Registries` that inspects `[assembly:KoanModule(...)]` and provider attributes, emitting a partial that registers modules into `IKoanAutoRegistrar` implementations.
2) Flag‑gate via `Koan:Features:GeneratedRegistries=true` and fall back to reflection when off.
3) Emit **BootReport** with generation hash for traceability. fileciteturn0file16

## Acceptance Criteria
- No reflection scanning on boot when flag is on.  
- AOT sample builds and runs with generated registries.
