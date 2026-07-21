# B4 — Post‑Quantum Cryptography (PQC) toggles (opt‑in)

**Intent**: Provide optional PQC algorithms (ML‑KEM, ML‑DSA/SLH‑DSA) where supported, gated by config/policy.  
**Why**: .NET 10 introduces PQC APIs; use them prudently. citeturn7search0turn7search4

## Plan
1) Add `Koan.Security.Pqc` with helpers: capability detect via `IsSupported` and surface **policy**: `Off | Warn | Enforce`.
2) Document platform caveats (CNG/OpenSSL availability). citeturn7search6

## Acceptance Criteria
- Sample signs/verifies with ML‑DSA when policy=Enforce on supported OS; falls back otherwise.
