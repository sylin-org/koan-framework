# Source Generators: Considerations (discussion)

Goal: Evaluate whether a source generator should create static facades over DI repositories (e.g., Person.Save) and other DX sugar.

Pros
- DX parity with static convenience methods; less ceremony in call sites.
- Can generate compiled queries/mappings for Dapper or EF to improve perf.
- Discoverability: can emit analyzers/code fixes suggesting registrations.

Cons
- Build complexity; slower inner loop and more moving parts.
- Debuggability: generated code can obscure stack traces.
- Versioning risk: generator changes can be breaking; maintenance overhead.
- Optionality: goes against "no magic by default"; may surprise users if auto-enabled.

Middle path (recommended)
- Start without a generator. Provide tiny hand-written static helpers as optional packages (Sora.Data.Facades) that call into DI-resolved repos.
- If demand is strong, add a generator later for convenience-only features (never required), behind an explicit opt-in MSBuild property.
