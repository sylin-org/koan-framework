# attic

Parked code that is no longer built or referenced by the solution. Kept for reference only.

- **S8.PolyglotShop** (parked 2026-06) — sample app that depended on the experimental
  `Koan.ServiceMesh` + `Koan.Service.Translation` projects, which were cut (assessment card C8:
  no ADR, no tests, experimental). The sample was already broken / out-of-solution before the cut.
- **Koan.Rag** + **Koan.Rag.Abstractions** (parked 2026-06) — ~8k-LOC RAG incubator with zero
  in-repo consumers (its `InternalsVisibleTo` pointed at a nonexistent `Koan.Rag.Tests` project,
  and the only inbound reference was a bootstrap smoke spec). Removed from the solution and parked
  here pending a real consumer (assessment card C10).
