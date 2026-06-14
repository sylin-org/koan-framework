# A5 — Blessed Container/AOT Lanes

**Intent**: Provide a frictionless path for Windows, Docker/K8s, GitHub Actions, and Azure DevOps with **publish-as-container** and AOT notes.  
**Why**: The SDK now builds container images directly; Native AOT can reduce size/latency when compatible. citeturn2search3turn2search12turn2search5

## Plan
1) Add docs + templates using `dotnet publish /t:PublishContainer` with `ContainerImageFormat` controls. citeturn9view0
2) Provide recommended base images per scenario; call out **AOT** and globalization needs. citeturn2search4
3) Guidance on **AOT compatibility** (reflection, codegen) and when to avoid. citeturn2search5turn2search8

## Acceptance Criteria
- Sample app publishes a working container image without a Dockerfile.  
- AOT guidance page linked from templates.

## Tests
- GH Actions and Azure DevOps pipelines that `dotnet publish /t:PublishContainer` and run smoke tests. citeturn2search6
