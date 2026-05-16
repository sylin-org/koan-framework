# A3 — JSON Strategy: Newtonsoft default; STJ strict where safe

**Intent**: Keep **Newtonsoft.Json** for global/runtime polymorphism (plugins, dynamic models). Use **System.Text.Json** only for **closed** DTOs (Minimal APIs, internal pipelines) and enable **strict duplicate-property rejection**.  
**Rationale**: STJ polymorphism requires declaring known derived types, or a TypeInfoResolver—good for closed worlds, insufficient for Koan’s open polymorphism. .NET 10 adds a setting to **disallow duplicate properties** to mitigate JSON ambiguity. citeturn3search0turn3search6turn0search4

## Plan

1. **Koan.Web (MVC)** stays on **AddNewtonsoftJson** by default; Minimal API endpoints use STJ. citeturn4search5turn4search4
2. Ship `Koan.Web.Json.Strict` with Koan auto-registrar + `Koan:Json:MinimalApis` binding to flip strict mode.
3. New option in `Koan:Json`:
   ```json
   {
     "Koan": {
       "Json": { "MinimalApis": { "DisallowDuplicateProperties": true } }
     }
   }
   ```
   Wire to `JsonSerializerOptions.AllowDuplicateProperties = false`. citeturn0search1
4. Provide a **TypeInfoResolver** hook for STJ when teams want compile‑time polymorphism in closed models. citeturn3search2
5. Document the **limitation** clearly (no runtime/open polymorphism in STJ). citeturn3search0

## Guardrails

- Koan entities and controllers remain Newtonsoft by default.
- Minimal endpoints: prefer value/record DTOs. If runtime polymorphism is needed, route through MVC/JSON.NET.

## Acceptance Criteria

- Minimal API sample rejects duplicate properties when enabled.
- Controller sample serializes derived members without per‑type registration.

## Tests

- Polymorphic roundtrip tests (Newtonsoft) with unknown subtype.
- STJ sample with `[JsonPolymorphic]/[JsonDerivedType]` passes when all subtypes are declared. citeturn3search6turn3search7
