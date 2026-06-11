# E10 — Koan Consumes Certmesh Trust; Tokens Bind to Machine Identity

**Repo(s)**: Koan · **Phase**: C · **Prereqs**: E06 (satellites exist), E08 (CSR certmesh)
· **One session** · Read [CHARTER.md](CHARTER.md) in full first.

## Mission

Give Koan its first certificate affordances — as a satellite, per the layering law — so a
Koan app can (a) **serve and call over channels trusted by a Koi pond CA**, and (b) **issue
and validate workload tokens bound to the machine identity certmesh attests**. This is the
binding STACK-0001 item 6 specifies: certmesh = machine/channel identity, Koan
Security.Trust/KSVID = workload/agent identity, joined by a claim — never merged, never
independent. Each fabric patches the other's hole: coherence-epoch revocation kills a
compromised workload's access in seconds while its transport cert ages out in ≤30 days.

## Context (verify each)

- Koan has **zero** X509/cert-validation code in `src/` today (verified grep, 2026-06-11);
  .NET trusts the OS store natively, and koi-truststore already installs pond CAs there
  (ZG's rake does `install_ca_cert(..., "zen-garden-pond")`). So OS-store trust works with
  no code; what's missing is *affordance*: explicit pinning, serving, and the token binding.
- The existing fabric: `KOAN/src/Koan.Security.Trust/` (ADR SEC-0001; `Identity.Current`,
  `IIssuer`, `TrustClaims`) — the repo's best-engineered small project. Read it and its ADR
  before designing; follow its conventions exactly.
- KSVID + coherence-epoch revocation are **planned, not shipped** (Koan assessment
  05-strategic-position capability #2). This prompt does NOT build KSVID; it defines and
  ships the *binding claim* shape so KSVID lands on it later.
- Certmesh material on disk (the conventions consumers use): CA + service certs under the
  Koi data dir (`fullchain.pem`, `key.pem`, cert layout `{data_dir}/koi/certs/{name}/`);
  enrollment is CSR-based after E08.

## DECIDED

1. **Everything lands in a satellite** — `Sylin.Koan.Trust.Certmesh` (or folded into the
   `Sylin.Koan.ZenGarden` satellite if Security.Trust's conventions argue for it — record
   the choice). Nothing mainline references certmesh; mainline Security.Trust stays
   substrate-agnostic.
2. **Channel affordances**: (a) an `HttpClient` configuration helper that pins a pond CA
   (PEM path or OS-store thumbprint) for outbound validation; (b) a Kestrel configuration
   helper that serves HTTPS from a certmesh-issued cert directory (watching for renewal —
   certs rotate every ≤30 days). Both fail loud with actionable messages when material is
   missing (the repo's fail-loud canon).
3. **The binding claim** (`TRUST-BINDING.md` convention doc, committed with the code):
   tokens issued by a bound issuer carry claim `koi_id` = the certmesh certificate identity
   (exact CN/SAN format pinned in the doc after reading certmesh's actual issuance), plus
   `koi_ca` = CA fingerprint. **v1 validation is CA-trust-only**: the inbound validator
   verifies the claim's CA fingerprint chains to a trusted pond CA. Proof-of-possession
   (claim-vs-peer-cert channel matching) is explicitly deferred until client-cert channels
   exist Koan-side — the doc states this honestly.
4. **Issuer enrichment**: an `IIssuer` decorator/extension in the satellite reads the local
   certmesh identity and stamps the claims; apps opt in by referencing the satellite
   (Reference=Intent).
5. The sovereign constraint: all of this works air-gapped — no online issuer, no OCSP/CT
   assumptions; trust roots are the pond CA file/OS store only.
6. **Guards**: integration tests with a throwaway CA (generate test CA + certs in-test):
   pinned-CA client accepts the good chain and rejects an untrusted one; Kestrel helper
   serves; issued token carries the claims; validator accepts good CA fingerprint, rejects
   unknown. ARCH-0079 applies (real `AddKoan()` composition in at least one spec).

## DEFAULT

- Claim names (`koi_id`, `koi_ca`) and fingerprint algorithm (SHA-256) — deviate only with
  justification recorded in TRUST-BINDING.md.
- Renewal watching: file-watcher on the cert directory; polling fallback.

## Plan of record

1. Read Security.Trust + SEC-0001 end to end; read certmesh issuance to pin the identity
format. 2. Write TRUST-BINDING.md (the convention is the deliverable the other repos cite).
3. Implement channel affordances; 4. issuer enrichment + inbound CA-fingerprint validation;
5. tests per DECIDED 6; 6. docs (satellite README with a 20-line "serve trusted, call
trusted, issue bound" sample — snippet-verified); 7. SURFACES.md row (guard = the new
integration specs).

## Verification

The test suite, plus one manual probe if a local Koi is available: enroll this machine,
point the helpers at the real cert dir, serve + curl with the pinned CA. If no local pond
exists, the throwaway-CA tests carry the verification and SURFACES notes "live probe
pending E11."

## Definition of done

- [ ] Satellite ships channel + token affordances; mainline untouched (arch test still
      green).
- [ ] TRUST-BINDING.md pins claim format + v1 CA-trust-only semantics + the PoP deferral,
      honestly.
- [ ] Integration specs green via real composition; SURFACES.md updated.
- [ ] Session summary states exactly what E11's demo can now assume.
