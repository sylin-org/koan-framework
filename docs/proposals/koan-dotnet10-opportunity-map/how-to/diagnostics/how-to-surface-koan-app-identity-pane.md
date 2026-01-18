# Surface Koan Application Identity Pane

**Contract**

- Inputs: Koan application targeting `net10.0` or later, `Koan.Core` package at `0.6.3`+ with identity pane support.
- Outputs: Boot diagnostics with a `[KOAN] Application` block listing identity, registry, inventory, and environment details.
- Error Modes: Missing attribute or configuration falls back to defaults (`Koan Application`, `koan-app`) and panes omit blank fields.
- Acceptance Criteria: Application identity metadata renders in a single pane and is consistent with configuration, Swagger/OpenAPI and provenance snapshots.
- Edge Cases: Attribute omitted, partial configuration overrides, large tag lists (>5 entries), non-web hosts.

## Steps

1. **Publish immutable identity metadata via assembly attribute.** Add a `KoanApp` attribute in the entry assembly (e.g., `Program.cs`) to set the human-facing values that rarely change per deployment:

   ```csharp
   using Koan.Core.Hosting.App;

   [assembly: KoanApp(
       Name = "Garden Cooperative",
       Description = "Neighborhood produce co-op slice showcasing Koan self-description.",
       Tags = new[] { "sample", "gardening" }
   )]
   ```

   - The attribute wins over assembly metadata and fills gaps when configuration skips a value.
   - Tags merge with configuration; duplicates are trimmed case-insensitively.

2. **Layer runtime overrides in configuration.** Bind the `Koan:Application` section for elements that vary per environment (e.g., code slugs, contact channels).

   ```json
   {
     "Koan": {
       "Application": {
         "Code": "garden-cooperative",
         "ContactEmail": "support@gardencoop.local",
         "SupportUrl": "https://samples.koan.dev/garden-coop",
         "Tags": ["cooperative"]
       }
     }
   }
   ```

   - The resolver slugifies `Code` and falls back to the attribute or normalized name.
   - Leave a field blank to let attribute/assembly defaults drive it.

3. **Verify the pane during startup.** Run the host (`dotnet run` or the sample `start.bat`) and inspect the boot diagnostics. You should see a block similar to:

   ```text
   ┌─ [KOAN] Application ────────────────────────────────────────────────────────
   │ Name        : Garden Cooperative
   │ Code        : garden-cooperative
   │ Environment : Development (SelfOrchestrating)
   │ Host        : ASP.NET Core (g1c1.GardenCoop)
   │ Session     : 6f836ed2
   │ Runtime     : Koan.Core 0.6.3.0
   │ Timestamp   : 2025-11-12T20:39:43.2319071+00:00
   │ Contact     : support@gardencoop.local
   │ Support     : https://samples.koan.dev/garden-coop
   │ Tags        : sample, gardening, cooperative
   │
   │ ─ Registry ────────────────────────────────────────────────────────────────
   │ Initializers: 17 (Koan.Web=8, Koan.Data=4, Koan.Core=3, Koan.Admin=1, +1)
   │ AutoReg     : 16
   │ Background  : 9 (startup=1, periodic=2)
   │ Adapters    : 1
   │
   │ ─ Inventory ───────────────────────────────────────────────────────────────
   │ Koan.Web    : 0.6.3.0
   │ Koan.Web.OpenApi: 0.6.3.0
   │ ...
   │
   │ ─ Environment ─────────────────────────────────────────────────────────────
   │ InContainer : false
   │ Process     : Started 2025-11-12T20:39:42.9175114+00:00
   │ Uptime      : 00:00:00.3210242
   │ Machine     : LEO-MAIN
   │ Health      : probes=11 (critical=3 optional=8) last=10ms overall=Healthy
   └─────────────────────────────────────────────────────────────────────────────
   ```

- If the block is absent, confirm `Koan.Core` is updated and the application calls `services.AddKoan()`.
  - Missing contact/support values simply hide the corresponding rows.

4. **Cross-check downstream consumers.** The same identity snapshot now flows to:
   - OpenAPI/Swagger document transformers (title, description, contact URL).
   - Environment snapshots (`KoanEnv.CurrentSnapshot.Application`).
   - Provenance exports (`Koan.Web.Connector.Swagger` provenance items).

## Notes

- The resolver tolerates non-web hosts; console-only apps still render `[KOAN]` when `Koan.Core` logging is enabled.
- Large tag lists are comma-separated; prefer 3–5 concise tags for readability.
- For secure contact URLs use HTTPS to avoid warning banners in generated docs.
