---
type: GUIDE
domain: framework
title: "Koan V1 Reorganization Current Handoff"
audience: [maintainers, ai-agents]
status: current
last_updated: 2026-07-15
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-15
  status: reviewed
  scope: R05 fresh-agent repeat and filtered-query repair complete
---

# Koan V1 reorganization current handoff

Replace this file at every handoff. It is a restart point, not a diary.

## Current state

- R04 remains passed; FirstUse remains the stable shortest executable result.
- R05 is `in-progress` under [`R05-BACKLOG.md`](R05-BACKLOG.md).
- R05-01 business spine and R05-02 reactive/agentic collaboration pass.
- R05-03's fresh package clean room passes. Two independent agent rehearsals confirmed the central
  journey; the deeper run returned `repair-and-repeat`, and all five bounded repairs now pass.
- A genuinely fresh repeat over candidate `47ce8915` again confirmed the complete FirstUse and
  GoldenJourney business paths, modern HTTP MCP, persistence, facts convergence, agent mutation, and
  V5 rejection/recovery. It returned `repair-and-repeat` on a smaller second-round truth queue.
- The first promoted finding is repaired: case-insensitive field binding now hands relational
  adapters the canonical resolved path, and the exact documented camelCase FirstUse filter is part
  of the executable source/package evidence.
- No public package was published and no branch was pushed by this work.

## What now exists

[`samples/GoldenJourney`](../../../samples/GoldenJourney/README.md) is one cumulative anonymous review
workflow. `ReviewRequest : Entity<ReviewRequest>` owns intake, assessment, and recommendation rules.
A business-named controller exposes only workflow actions. Two bounded MCP tools list assessed work
and record a non-final recommendation. `Program.cs` remains the complete four-line `AddKoan()` host.

The source proof runs eleven observable steps across three isolated starts:

1. startup/health;
2. operator composition facts;
3. persisted REST result;
4. MCP initialization;
5. bounded tool discovery plus byte-identical agent/operator facts;
6. stable agent rejection before assessment;
7. durable assessment with Critical priority and completed 100% progress;
8. honest non-executing custom-tool dry-run;
9. agent recommendation observed through REST;
10. unavailable default-adapter rejection with stable correction; and
11. clean restart with restored SQLite election.

FirstUse and GoldenJourney now share process lifecycle, isolated SQLite, MCP negotiation, bounded
readiness, failure logs, and cleanup primitives. Their business probes and evidence remain separate.

## Verified this session

- Jobs: in-memory 76/76 and SQLite 78/78, including terminal progress and composition.
- GoldenJourney source cumulative contract: 1/1, including rejection/recovery.
- FirstUse source contract after shared-harness migration: 1/1; eight steps preserved.
- Packaging compiler build: 0 warnings / 0 errors.
- Packaging suite: 15/15, including both serialized executable application contracts.
- Fresh Git-derived release rehearsal at disposable source commit
  `540a84c9b4339458c69362cbd1c0aae8b8bc4668`: 113 package owners evaluated; 84 packages selected,
  packed, and verified (45 version changes plus 39 unpublished-current registry repairs).
- Fresh external FirstUse passed 8/8 in 4.129s and GoldenJourney passed 11/11 in 8.769s; both
  restored/built from the hydrated package feed with zero warnings and zero errors. Evidence is under
  ignored `artifacts/r05-fresh-release-540a84c9b433/`.
- Disposable mixed-closure package rehearsal: FirstUse 8/8 in 4.793s and GoldenJourney 11/11 in
  10.242s on .NET 10.0.9 / Windows 10.0.26200; both external restores/builds emitted zero warnings and
  zero errors. This remains diagnostic implementation evidence; the fresh gate above supersedes it.
- GoldenJourney solution membership and Release project build: 0 errors; two existing Koan.Web XML
  documentation warnings appear when that dependency rebuilds.
- Full Release solution build: 0 errors / 30 existing warnings.
- Independent agent rehearsal: the exhaustive reader reproduced FirstUse, GoldenJourney, REST, MCP,
  persistence, Jobs, and converged facts, then identified three responsible repairs. A second,
  narrower reader independently passed the business path and corroborated the missing V5
  reproduction and warning noise, but did not exercise readiness or MCP resources deeply enough to
  overrule the first result.
- Readiness repair commit `977f33b9`: connector availability is now distinct from application
  dependency; inactive bundled JSON reports `Unknown` without touching disk, selected JSON follows
  repository auto-provisioning, and host-owned Entity diagnostics no longer reflect over a removed
  cache field. Data.Core passes 301/301, JSON 19/19, both executable journeys use `/health/ready` and
  pass, strict docs build passes, and the package inventory still recognizes 113 owners.
- MCP self-description repair commit `c9977361`: one custom-tool projection now drives protocol
  listing, remote dispatch, Explorer, and `koan://self`. The self resource exposes only the caller's
  usable Entity and custom-workflow surface, while `koan://entities` remains Entity-specific. MCP
  conformance passes 73/73, the live GoldenJourney source contract passes 1/1, and strict docs pass.
- Composition-lockfile repair commit `a2780672`: Core's target is now a `buildTransitive` asset, the
  two supported source contracts receive the same target centrally, and both application probes fail
  if their checked-in lockfile is missing or incomplete. The release compiler rejects a Core artifact
  without that asset. A fresh 84-package clean room passed: external FirstUse 8/8 in 4.755s and
  GoldenJourney 11/11 in 8.754s, both with `compositionLockfileObserved=true` and zero build warnings
  or errors. Evidence is under ignored `artifacts/r05-lockfile-release/`.
- V5 reproduction repair commit `775d5716`: GoldenJourney publishes one exact test command plus a
  manual PowerShell rejection/recovery sequence, names the stable fact/reason/correction surface, and
  explains why an Entity-level SQLite route outranks the rejected application default. The command
  passes 1/1 and strict docs passes.
- Quiet/current first-use repair commit `ffc1ed27`: eight traversed compiler warnings are corrected,
  both supported source contracts build warning-as-error clean, MCP's front door now teaches
  Reference = Intent and `POST /mcp`, and the public path includes a SQLite-verified encoded-JSON
  REST filter. FirstUse passes 1/1, GoldenJourney's exact public command passes 1/1, the focused
  filter contract passes 1/1, and strict docs pass. Adjacent design debts are preserved in
  [`POST-CYCLE-TODO.md`](POST-CYCLE-TODO.md).
- Fresh-repeat filtered-query repair: `ResolvedField` now retains the canonical member path and the
  shared relational translator uses it for case-sensitive JSON extraction. SQLite convergence passes
  lowercase and mixed-case field probes; the FirstUse contract now asserts the exact documented
  camelCase filter and passes 1/1 after a warning-as-error Release build; the filtering suite remains
  green at 92/92.

## Important discoveries

- Terminal Jobs settlement previously overwrote handler progress with the stale claimed record; fixed
  and protected in the shared behavior suite.
- Unavailable configured data adapters now produce a rejected `data:default` fact with stable reason
  and correction instead of an unexplained fallback.
- Custom MCP result property casing differs from REST web casing. The proof is tolerant; changing this
  pre-1.0 wire behavior requires a separate compatibility decision.
- Custom imperative mutations cannot offer a truthful full dry-run; Koan reports a non-executing
  partial rehearsal instead.
- API-only applications no longer emit a missing-web-root warning; both executable journeys gate the
  quiet-startup behavior while static files remain enabled when a real file provider exists.
- Replaying against the older R04-04 closure failed immediately with the expected Data.Abstractions
  ABI mismatch. Replaying from the later R04-08 verified closure plus rebuilt Data.Core/Jobs/Web
  passed both applications, proving stale closure mixing cannot masquerade as success.
- A linked worktree cannot currently host the release compiler because repository discovery accepts
  only a `.git` directory; a clean disposable clone works. Long full-graph runs also buffer child
  output enough to look quiet between packages. Both are bounded packaging UX follow-ups.
- Package references to the foundation bundle compose JSON as an available fallback. Before
  `977f33b9`, its missing default directory made a SQLite-selected application return readiness 503.
  Health now gates only default-elected, configured, or observed providers; availability alone is
  inspectable but inert.
- The independent run's five material findings are repaired in `977f33b9`, `c9977361`, `a2780672`,
  `775d5716`, and `ffc1ed27`. Broader small issues are explicitly deferred in
  [`POST-CYCLE-TODO.md`](POST-CYCLE-TODO.md), not silently discarded.
- The fresh repeat exposed why the earlier focused REST-filter test was insufficient: it used the CLR
  spelling `Name`, while the public API correctly used web-style `subject`. Resolution was
  case-insensitive, but SQLite extracted the caller spelling from case-sensitive stored JSON and
  silently returned an empty result. The shared canonical-path handoff fixes the relational family
  rather than special-casing FirstUse or rewriting the public command.

## Next safe action

Repair the fresh repeat's composition truth mismatch next: runtime module discovery currently counts
the application assembly as a module while the build lockfile correctly records it only as `app`, so
both supported samples report false `+Koan.*` drift. Prove a no-drift live sample contract, then move
to the bounded readiness/operator and MCP discoverability findings. Obtain the remaining human
rehearsal only after this second repair queue is quiet. Do not begin the post-cycle todo register
unless new evidence promotes an entry into a correctness, security, or release blocker.

## Do not infer

- The public NuGet set is still not a supported install path.
- Source success is not package-only success.
- GoldenJourney does not certify distributed Jobs transports, hostile-client security, every adapter,
  full custom-tool rehearsal, or production authorization design.
- The agent recommendation is deliberately non-final.
- Private downstream observations remain questions only; repository evidence stays anonymous.

## Repository state

The coherent R04/R05 candidate is `d1dbbe35`; the first independent-rehearsal repair sequence ends at
`ffc1ed27`, and its closure record is `47ce8915`. This commit contains the fresh-repeat filtered-query
repair. Only evaluator reports remain untracked under `tmp/`. Do not stage those reports, or publish,
push, tag, or release the candidate without a separate operator request.
