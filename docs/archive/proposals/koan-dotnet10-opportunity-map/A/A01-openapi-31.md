# A1 — OpenAPI 3.1 as Default (Microsoft.AspNetCore.OpenApi)

**Intent**: Move Koan’s API description to **OpenAPI 3.1** by default using `Microsoft.AspNetCore.OpenApi`; retire prior per-endpoint `WithOpenApi()` usage (now deprecated). Keep Swagger UI optional.  
**Why**: Better JSON Schema compatibility (draft 2020‑12), simpler native generation, less boilerplate. citeturn6search0turn6search1

## Plan (agent-ready)
**Touch modules**: `Koan.Web`, `Koan.Web.Swagger` (UI only), new `Koan.Web.OpenApi`. fileciteturn0file15  
1) Add new module **Koan.Web.OpenApi** with an **auto-registrar** that calls:
   - `builder.Services.AddOpenApi();` (if app uses Minimal APIs)  
   - `app.MapOpenApi();` to expose `/openapi/{docName}.json`  
   - Keep controller-based APIs supported—generation is unified. citeturn6search5
2) Update `Koan.Web.Swagger` to depend on **Koan.Web.OpenApi** and only wire UI (Swashbuckle or RapiDoc) if requested. 
3) Audit and **remove legacy `.WithOpenApi()`** extensions in templates/samples. citeturn6search1
4) Ship a **migration note** and boot report entry: “openapi: v3.1 via Microsoft.AspNetCore.OpenApi”.

## Guardrails
- Minimal defaults; UI not enabled unless `Koan:OpenApi:Ui:Enabled=true`.  
- Keep **escape hatch**: allow custom document transformers. fileciteturn0file16

## Acceptance Criteria
- `/openapi/v1.json` returns **3.1** documents for Minimal and MVC apps. citeturn6search0  
- No use of deprecated `WithOpenApi()` remains. citeturn6search1  
- Swagger UI works when `Koan.Web.Swagger` is referenced.

## Tests
- Golden-file compare of OpenAPI output for sample apps.  
- Template E2E: build, run, fetch OpenAPI, smoke Swagger UI.
