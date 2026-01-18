# A6 — JsonPatch (System.Text.Json) option

**Intent**: Offer JsonPatch via STJ for Minimal APIs to reduce Newtonsoft coupling in PATCH scenarios, while keeping MVC/Newtonsoft as the default.  
**Why**: .NET 10 ships a STJ-based JsonPatch package. citeturn4search3

## Plan
- New package `Koan.Web.JsonPatch.STJ` (auto-registrar) that wires `Microsoft.AspNetCore.JsonPatch.SystemTextJson` when enabled via config flag.

## Acceptance Criteria
- Minimal API sample applies a patch with STJ.  
- MVC path still supported with Newtonsoft when selected.
