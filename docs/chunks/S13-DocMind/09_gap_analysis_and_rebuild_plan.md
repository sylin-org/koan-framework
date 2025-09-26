# Gap Analysis & Rebuild Plan Refresh

## Bootstrap & Infrastructure
- ✅ **DocMindRegistrar ships with the sample**: no bespoke `AddDocMind()` extension is required. Focus on verifying it loads during `AddKoan<DocMindRegistrar>()` and produces the expected boot report sections.
- 🔄 **Minimal Program.cs**: keep the file lean—`AddKoan<DocMindRegistrar>()`, health/MCP middleware, and controller mapping only. Any additional configuration should be moved into the registrar or feature-specific modules.

## Developer Experience Updates
- 📦 **Package Guidance**: document that including the `DocMind` package pulls in the registrar automatically. Highlight how it discovers background services, AI options, and telemetry.
- 🧭 **Onboarding Checklist**: update runbooks to instruct engineers to check the registrar wiring (instead of writing a new extension). Include troubleshooting notes for scenarios where the registrar assembly is missing from the probing path.

## Action Items
- [ ] Update infrastructure documentation (Chunk 05) to reference the shipped registrar and minimal bootstrap.  
- [ ] Revise migration steps (Chunk 08) so they confirm registrar loading rather than authoring one.  
- [ ] Refresh orchestration scripts/checklists to assert `DocMindRegistrar` availability.  
- [ ] Align DX collateral (README, agent instructions, alignment assessment) on the registrar-first bootstrap story.

## Risks & Mitigations
- **Risk**: Teams attempt to re-implement the registrar because older docs referenced `AddDocMind()`.  
  **Mitigation**: Emphasize the packaged registrar in all checklists and provide quick diagnostics (`dotnet run -- --boot-report`).
- **Risk**: Divergent environments disable MCP transports unintentionally.  
  **Mitigation**: Keep the Program.cs sample explicit about enabled transports and point to registrar hooks for overrides.

## Next Steps
- Conduct a short pairing session with DX to validate the refreshed quickstart.
- Backport the registrar messaging into proposal alignment and release notes.
- Schedule a follow-up review after the next nightly build to confirm automated smoke tests exercise the registrar flow.
