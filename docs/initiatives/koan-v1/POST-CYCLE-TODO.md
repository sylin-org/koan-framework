---
type: GUIDE
domain: framework
title: "Koan V1 Post-Main-Cycle Todo Register"
audience: [maintainers, architects, ai-agents]
status: current
last_updated: 2026-08-20
framework_version: v0.20.0
validation:
  date_last_tested: 2026-08-20
  status: reviewed
  scope: bounded design and polish debt deliberately deferred from active V1 slices
---

# Koan V1 post-main-cycle todo register

## Contract

This register preserves small but real issues that deserve deliberate treatment after the active
V1 slice. It prevents two failure modes: widening the active repair until it never
finishes, and losing a design concern because the immediate warning or symptom was made quiet.

An entry is not authorization to change a public contract. Before implementation, give it a bounded
card, apply the normal exploration workflow, decide compatibility explicitly, and name executable
acceptance evidence.

**Verify the premise against the tree first.** A deferred entry records the tree as it was on the day it
was written, and unrelated work closes these silently: on 2026-08-20, four of the eight entries examined
had already lapsed, two of them retired by a single commit (`a39edffa4`) that nobody reconciled back
here. Treating a stale premise as work is how a register becomes a source of invented tasks. Beware
`bin`/`obj` left in `src/` by an earlier build — an ignored directory can make a retired package look
live; `git ls-files` is the authority, not `ls`. If new evidence makes an entry a correctness, security, or release blocker,
promote it into the active backlog instead of waiting for this list.

## Current register

| ID | Surface | Deferred issue | Why it stays out of the main cycle | Decision required before work | Acceptance evidence |
|---|---|---|---|---|---|
| PMC-006 | Release tooling | Long package plans capture child output and can appear idle between package completions. | Buffered output does not weaken artifact evidence, but it makes operator supervision unnecessarily uncertain. | Define concise live progress events without leaking secrets or making resumable state depend on console rendering. | A bounded slow-process test proving periodic progress, failure context, and unchanged machine-readable evidence. |
| PMC-008 | Data / vector transactions | `VectorSaveOperation` crosses the Data.Core→Vector boundary through reflection, method-name lookup, and a runtime `Task` cast; nearby error text still says `UpsertAsync` while lookup uses `Upsert`. | The nullable-array correction is behavior-preserving; replacing the bridge changes a cross-project contract and transaction behavior. | Decide whether a small Core-owned capability seam can remove reflection without reversing dependency direction; otherwise make the reflection contract explicit and fail-loud. | Transactional vector save/delete integration proof, missing/incompatible method mutations, cancellation, and non-atomic reporting. |
| PMC-009 | Documentation tooling | XML documentation defects can remain invisible until a public sample rebuild happens to traverse the owning project. | The supported contracts now reject warnings, but repository-wide doc-link validation belongs with the broader warning policy. | Decide whether packable projects or the solution should treat XML-doc reference warnings as errors and how generated/legacy code is scoped. | A deliberately broken `cref` mutation fails the selected CI lane; current shipping modules pass it. |
| PMC-020 | Certification evidence | The durable aggregate manifest now exists: every project emits a TRX and `scripts/aggregate-test-evidence.ps1` merges them into `artifacts/ratchet/test-manifest.json`. **What remains is CI parity** — no CI lane runs the ratchet at all, so there is no protected run to compare a local manifest against. Certification is a manual milestone by design (ARCH-0121), which is why red accumulates unseen: the first full run in some time was RED with 13 failing suites. | Adding a scheduled ratchet lane is a real compute and noise decision, not a mechanical follow-on. Many suites are container-backed, and a flaky nightly gate that gets ignored is worse than none. | Decide which lane CI runs (every suite, or the docker-free subset), on what trigger (nightly cron as `skills-verify.yml` already does, or on demand), and whether a red result notifies or merely records. Then upload the manifest as a build artifact so local and CI shapes can be compared. | A protected run publishes `test-manifest.json`; its aggregate agrees with a local run over the same commit and suite set; a deliberately failing suite is named identically in both. |
| PMC-021 | Entity Communication authoring | `[EventDetailsRequired]` makes payloadless `Raise<TEvent>()` fail before source enumeration at runtime, but no build-time diagnostic guides the author at the call site. | R07-08 deliberately keeps one Communication package and avoids introducing an analyzer assembly for a misuse that already fails safely and clearly. | Decide whether this friction occurs often enough to earn a shared Koan analyzer rule, and define generic/event-attribute resolution without coupling analyzers to runtime discovery. | A compile fixture rejects payloadless Raise only for details-required event kinds, accepts optional and explicit-details calls, supplies one corrective message, and leaves runtime enforcement unchanged. |
| PMC-022 | Media derivative lifecycle | Public `MediaDerivation` exposes framework storage mechanics, keys persisted output by source id plus recipe fingerprint without source-contract identity, and requires applications to query framework rows for targeted cleanup or statistics. | R07-17 removes a false generic sweep safely, but a correct replacement needs one context-aware render/lifecycle owner and an explicit migration rather than another speculative storage SPI. Current on-demand and targeted application behavior remains honest. | Centralize derivative identity, storage, access, and cleanup behind a Media-owned coordinator keyed by source contract/type and logical context; decide compatibility and migration for existing rows before hiding framework storage. | Multi-source-type collision and isolation proofs, tenant/access context safety, targeted cleanup and statistics, HTTP/direct-render convergence, restart behavior, and an explicit migration fixture for existing derivatives. |
| PMC-023 | Entity Communication evolution | Distributed receiver groups and wire contracts currently derive identity from CLR application types. A type rename changes the group/contract identity; there is no stable alias, schema-version negotiation, or heterogeneous application manifest. | R07-18 completes one-application business-channel policy and real RabbitMQ carriage. Inventing aliases before an integration/rolling-upgrade use case would add a second naming system without proving migration semantics. | Define stable contract and receiver-group aliases, ownership, version compatibility, rollout precedence, collision handling, and whether aliases belong on business types, handlers, or host composition. Keep cross-application integration distinct from Entity replication. | A rolling two-version RabbitMQ fixture proves compatible old/new participants, rename migration, collision rejection, exact startup/facts manifests, authenticated context, and fail-loud incompatible schemas without duplicate group delivery. |
| PMC-026 | Package impact precision | Analyzer ProjectReference sources conservatively mint every consuming package when the analyzer changes, even when a generator emits no source for a particular consumer or a diagnostic-only analyzer cannot change its assembly bytes. | Conservative automatic impact is release-safe and operator-free; proving output-sensitive selection requires compiler evidence and must not reintroduce a maintained package list or stale identity risk. | Decide whether generator-emission fingerprints can safely narrow release impact while diagnostic analyzers retain an intentional policy, with conservative mapping as the fail-safe default. | A generator change selects every consumer whose compiled/generated bytes can change, leaves proven non-emitting consumers untouched, handles diagnostics/config changes deliberately, and mutation tests reject any false negative. |
| PMC-029 | First-use startup truth | The exact R08-04 package console completed persistence/query work but reported two framework-owned collection failures. The repair is implemented: `StartKoan()` now owns one standard Generic Host lifecycle, focused host/Communication suites pass 8/8, a source-equivalent template console starts hosted capabilities and completes Entity work without either failure, and a focused Data.Core nupkg carries the required Host dependency. | Rebuilding the complete 108-package bootstrap solely to repeat package execution would spend a release-certification wave after the owner and consumer proofs are already green. A truthful Diagnostics section may still contain useful elections, guarantees, and corrections; the defect is a false `CollectionFailed` fact, not the existence of inspectability. | Keep [ARCH-0119](../../decisions/ARCH-0119-one-console-host-lifecycle.md) as the lifecycle owner. On the next exact candidate, make the existing package-only probes reject Communication-composition and health-registry collection failures before any public promotion. | The next exact package-only console, FirstUse, and GoldenJourney contain no false Communication or health-registry collection failure; focused tests continue to prove standard host services, hosted start/stop, real corrective faults, and clean disposal. Truthful non-failure Diagnostics remain visible. |
| PMC-030 | AI adapter lifetime | `IAiAdapterRegistry.Add` no longer exists; the registry is compiled once via `Compile(IEnumerable<IAiAdapter>)`. `InMemoryAdapterRegistry` still retains every compiled adapter — including disposable ONNX sessions — and implements no disposal, so host-shutdown ownership of adapter lifetime is still unstated. Premise re-verified 2026-08-20; the surface changed, the ownership question did not. | R10-06 moves contributor execution into `AiModule.Start` and proves process behavior; resolving mixed adapter ownership requires a deliberate contract rather than sample teardown code or an ONNX-only exception. | Choose one ownership model: registry-owned registrations, explicit ownership metadata, or DI-created adapters only. Align contributor construction and removal semantics before adding disposal. | Repeated-host ONNX and DI-owned-adapter fixtures prove exactly-once disposal, removal disposal policy, failed-contribution cleanup, no retained inference sessions, and unchanged routing/provenance behavior. |
| PMC-034 | Web Auth / Identity factors | The retired Credentials, Passwords, and MFA packages contained real BCrypt, TOTP, recovery, `amr`/`acr`, and checkup mechanics, but no supported authentication ceremony connected primary proof, step-up continuation, factor verification, and cookie issuance. The former specs manufactured proof claims and redispatched the lifecycle, while a real provider callback could be aborted by an untranslated gate exception. | Shipping or polishing those packages would turn application-auth scaffolding into a falsely complete framework claim. R11 removes the partial surface so V1 stays semantically honest; the dated SEC-0007 decision remains historical input. | Design one Web Auth-owned ceremony engine for external and Koan-managed primary factors. Put cross-module factor vocabulary in an inert contracts assembly; keep factor storage/mechanics in opt-in leaves; define continuation secrecy/lifetime, return handling, local-login collision and lockout, distributed single-use semantics, rate limiting, CSRF/origin posture, data-protection key persistence, enrollment/recovery authorization, startup/facts/health, and explicit unsupported guarantees. | A package-only application adds Passwords and/or MFA by reference plus existing `AddKoan()`; real controller/provider round trips prove password-only success, enrolled-MFA interruption and resume, TOTP/recovery single use under concurrency, callback continuity, restart/key behavior, lockout/rate limiting, CSRF/origin posture, no session before all factors, standard session/role projection after completion, and matching startup/runtime facts. |
| PMC-035 | Identity × Tenancy invitations | The former invitation record and acceptance service were retired together. Their check-then-write flow could grant one token to two different people under multi-node contention; verified-email matching also depended on upstream assurance and was not itself an inbox-ownership ceremony. | A process-local keyed lease would hide rather than solve the distributed claim problem; fail-closed pre-claim without recovery could consume an invitation without creating its seat. R11 keeps no placeholder type for a guarantee the framework does not complete. | Design one explicit invitation claim state machine with claimant identity, attempt/accepted timestamps, provider-independent conditional-write requirements or transaction ownership, idempotent recovery after partial failure, token hashing/uniqueness, expiry/revocation races, verified-address assurance policy, role validation, audit, and controller/browser security posture. | Two identities racing one token across multiple hosts yield exactly one seat and one durable claimant; retries recover every injected failure boundary without double grant or lost invitation; token/revocation/expiry/email-assurance/role cases fail correctly; package reference plus existing `AddKoan()` exposes one supported acceptance ceremony with startup/runtime explanation. |
| PMC-037 | Couchbase / comparable encoding | Couchbase is the one adapter DATA-0100's comparable encoding never reached. It stores a `TimeSpan` in .NET's default form, so `1.00:00:00` sorts before `23:00:00` — twenty-four hours ahead of twenty-three, the exact inversion that contract exists to close. Reproduced by `SortPushdownConvergence` on 2026-08-20: every other store orders the corpus correctly and Couchbase returns `d,e,c,b,a` where the framework gives `d,e,a,c,b`. Range filters on the same values are wrong for the same reason. | The encoding is order-preserving *storage*, so correcting it changes what is written. Existing Couchbase documents hold the string form, and a reader has to keep understanding them while writers move to ticks. `ComparableScalarEncoding` also lives in `Koan.Data.Relational`, which a document adapter should not reference, so the contract needs a shared home before Couchbase can honour it. | Decide the home for the encoding (Core, most likely) and whether moving it stays source-compatible for the four relational runtimes; then decide the read path for documents already written — tolerant reader, versioned field, or an explicit migration. | `SortPushdownConvergence` admits `Duration` to `PortableScalars` and passes on every adapter; `TimeSpan` returns to `IsPortableStreamSortScalar` with the streaming oracle green; a document written before the change still reads back equal. |
| PMC-044 | Jobs / cold-run timing assertion | `HighVolumeScanShapeSpec.bulk_save_of_a_large_batch_is_a_single_batched_write` asserts a fixed wall clock (`< 10000ms`) and fails on a cold run while passing on a warm one — measured 2026-08-21 at `a6210e2e9` with none of the current work present: 14.6s on the first run, under the bar on the second. A threshold that depends on JIT and page cache reports a defect where there is none, and the cost lands on whoever next reads a red suite: it consumed a full worktree-bisect to rule out as fallout from unrelated work. | The intent is real — the spec exists to catch a bulk save degenerating into per-row writes — so deleting the assertion would lose a genuine guard. Replacing wall clock with the thing actually being asserted needs the adapter to expose a count, which is a small surface decision rather than a test edit. | Decide what the spec should measure: a statement or round-trip count the adapter reports, a ratio against a single-row baseline measured in the same run, or a threshold generous enough that a cold run cannot cross it. Prefer the first — it states the claim ("one batched write") instead of approximating it. | The spec fails when a bulk save degenerates into per-row writes, and passes on a cold run on a loaded machine. |
| PMC-048 | Jobs / an unreproduced double-claim failure | `concurrent_claimers_take_distinct_jobs_no_double_claim` failed once on the SQL Server jobs suite on 2026-08-21, during the pass that moved that adapter off Dapper onto the raw-ADO surface. It did not reproduce: three isolated runs and three further full-suite runs all passed, and the full de-Dapper regression passed afterwards. One occurrence in four full runs. | The assertion is a correctness property — two claimers must not take the same job — so this is recorded rather than dismissed, and it is not diagnosable while it will not reproduce. It is also the suite where a one-off failure earlier the same day turned out to be a real tie-order defect rather than a flake (PMC-042), which is the reason not to assume timing. | Establish first whether the CAS claim path can admit two winners at all under the new parameter binding — the change replaced Dapper's binder, and a guard that compares a lease timestamp is exactly where a binding difference would show. If the loop is provably single-winner, the entry closes as an environment artifact; if it is not, the spec needs to fail against the unfixed loop. | Either a focused spec that fails against a loop admitting two claimers, or a demonstration that the claim is a conditional write no binding difference can weaken - with the reasoning recorded, not just a green suite. |
| PMC-053 | SQLite / a recurring unexplained failure cluster | The SQLite connector suite has three times reported a cluster of failures — four to nine at once, always file-backed specs — and passed 49/49 on immediate re-run every time. Two occurrences had an established cause (a concurrent foreground build; a worktree built at an older commit, both clobbering the shared test output). The third had neither. **Investigated 2026-08-21 and bounded, not solved.** Eliminated: test parallelism is already disabled assembly-wide; the fixture's database path is a per-run GUID, so no cross-process collision; in-memory sources are named from a per-instance fingerprint with pooling off, so `ClearAllPools` in `SqliteConfigurationTruthSpec` cannot reach them; and **the suite has fault containment** — a deliberately failed spec holding a live host against the shared store leaves the other 48 passing, so a cluster is N independent failures rather than one root cascading. Not reproducible in 6 isolated runs or 3 build-then-test cycles. | What survives is an environmental cause that hits several independent file-backed specs at once, which fits every occurrence: all three were long batches with Docker containers running, and none reproduced on a quiet machine. That is a hypothesis about the host, not the code, and the suite has now been shown structurally sound in the four ways that would have made it the culprit. Inventing a fix for a mechanism nobody has observed would be a blind patch with an architectural costume. | Nothing to decide until the next occurrence is captured **with its exception**. All three were seen through a summary filter that discarded the error text, which is why five hypotheses had to be eliminated by inspection instead of read off a stack trace. Any batch run of this suite must preserve full output or the TRX. | The exception behind a cluster is captured and named. If it is an IO or handle error under load, the entry closes as an environment interaction with that named; if it is anything else, the elimination list above says where not to look again. |
| PMC-051 | AOT / only win-x64 is measured | PMC-049 proved all five relational backends publish and run under NativeAOT on win-x64. Linux and the linux-arm64 edge RID — the appliance story the sovereign tier is justified by — are unmeasured for the server adapters. | The tempting inference is that the framework is RID-agnostic, so Linux follows. ARCH-0093 made exactly that argument in 2026-07 and it has since been shown to rest on a result that decayed: the win-x64 proof it cited stopped being true three weeks later and nobody noticed. An inference from a proof that expired is not evidence, and the cross-ILC toolchain for arm64 is genuinely different work from a native win-x64 publish. | **Decide build-output isolation before the first publish, not after a confusing result.** A Linux publish writes into the same `bin`/`obj` paths the Windows tree uses and would clobber them; this session lost two runs to exactly that hazard, once from a concurrent build and once from a worktree built at an older commit. `--artifacts-path` redirects but still shares the working tree, so one command without the flag reintroduces the clobber; `git archive HEAD` into the container is what ARCH-0093's own Debian verification did and leaves the host tree untouchable. Then decide whether Linux x64 is measured first as the cheap confirmation, or arm64 directly as the case that actually matters for the edge claim — and whether a Linux failure would be a framework defect or a provider one, since `Microsoft.Data.SqlClient`'s globalization constraint may behave differently against a container's ICU. | A publish-and-run per connector on linux-x64 following the same procedure as PMC-049, and a named blocker with its provider and error for anything that fails. This also closes PMC-050's residual gap: its four server cells run on demand rather than on a schedule, because GitHub-hosted Windows runners cannot host the Linux database containers they need — a Linux publish would let those four cells run on `ubuntu-latest` with service containers, turning the half of the matrix that is still manual into the half that is not. Only then is the RID-agnostic claim evidence rather than inference. |
| PMC-054 | SQL Server / a store that describes nothing cannot notice drift | `SqlServerDdlExecutor.Describe` reports column **presence only** — it returns a null state for every column and compares no definition at all, by a decision PMC-040 recorded deliberately. SQL Server is one of only two stores that build projected columns (`AS ... PERSISTED`), and it is the one that cannot tell a current projection from one an older Koan built. The mechanism that made this matter on MySQL is identical: a dialect changes how it reads a JSON scalar, new tables get the new expression, existing tables keep the old one, and the optimizer stops substituting a computed column whose expression no longer matches the query — silently retiring every index over it. No SQL Server expression has changed yet, so nothing is wrong today; the store simply has no way to find out when one does. | Starting to compare definitions judges every existing database against expectations no release has held it to, which is the reason presence-only was chosen. Marking is narrower than comparing — it judges only columns Koan itself computes — but on an existing database *every* projected column lacks a marker, so the first upgraded boot reports drift on all of them. That is honest (Koan genuinely cannot verify them) and is now self-clearing, since PMC-052 rebuilds exactly this class of column under the consent that created it — but it is still a behaviour change on upgrade and belongs to its own proof. | Decide the marker mechanism: SQL Server has no inline column comment, so the analogue of MySQL's `COMMENT` is an extended property (`sp_addextendedproperty`, read back through `sys.extended_properties` joined on `major_id`/`minor_id`), which is a second statement per projected column and needs a stated lifetime when the column is dropped or rebuilt. Reading `sys.computed_columns.definition` back is the alternative and is the one MySQL rejected, because a store returns its own canonical rendering rather than the text it was given. Decide too whether an absent marker reports drift — MySQL says yes, and matching it keeps one rule across both stores rather than a second dialect of the same idea. | A SQL Server table whose persisted computed column was built from an older expression is named on boot and rebuilt under `AutoCreate`, validating clean afterwards with the index over it used again; a column Koan wrote in the same release validates clean without a rebuild; and a non-projected column that drifted is still refused rather than rewritten. |

## Promoted and resolved history

- **PMC-052 — drift that could be repaired is repaired now, and only where repairing is safe.** Opened and
  closed on 2026-08-21. `EnsureCreatedAsync` added columns a table was missing and treated every other
  difference as something to report or refuse, so an upgraded MySQL database reported Degraded forever over a
  stale generated column that Koan knew the correct expression for (PMC-045).
  The boundary is the whole decision, and it is narrow on purpose: **a projected column, and nothing else**. A
  projected column holds no value of its own — the store recomputes it from the structured document on every
  write — so rebuilding one loses nothing whatever it had drifted into. Every other column holds its own value,
  and replacing one is how a framework destroys data by being wrong about what it is looking at. The structured
  root is the sharpest case and is refused rather than rebuilt, which the dockerless spec pins directly.
  Consent is the consent that already exists. Repair rides `IsDdlAllowed` — the same gate that lets Koan create
  the column in the first place — rather than earning a second knob. A store that may not issue DDL still gets
  the finding, and its reads still resolve through the document. Repair also precedes index creation, so an
  index is never built over an expression about to change; MySQL's `MODIFY COLUMN` rebuilds the indexes over
  the column it restates, which is what brings back the indexes the stale expression had quietly retired.
  `IRelationalDdlExecutor.RebuildProjection` is the one shared addition, defaulting to a refusal the same way
  `CreateIndex` does: the orchestrator asks only a store that answered `SupportsPersistedComputedColumns`, and
  a store that computes a column it cannot restate is a contradiction worth hearing about. Only MySQL and SQL
  Server project at all, and only MySQL can currently see that a projection is stale — **SQL Server describes
  its columns by presence only and compares nothing**, so it cannot yet detect the drift this repairs. Giving
  it the marker is the follow-on, and it is a better change now than before: with repair in place the first
  boot after an upgrade fixes what the marker finds instead of reporting it forever. Carried as PMC-054.
  Proof: the MySQL spec seeds a table, rewrites its generated column to the pre-PMC-038 expression with no
  marker, boots, and asserts the report is Healthy, that the column comment went from empty to `koan-gen:`
  (which only the ALTER writes, so the report cannot pass by validation merely giving up), and that a write
  round-trips afterwards. Three dockerless specs pin the decision itself: rebuilt under `AutoCreate`, reported
  and untouched under `NoDdl`, and refused when the drifted column is the structured root.

- **PMC-045 — a generated column can go stale without its type changing, and nothing said so.** Opened and
  closed on 2026-08-21. Fixing the JSON-null read changed the expression MySQL's stored generated columns are
  built from. New tables got the new expression; a table an earlier Koan created kept the old one, so on an
  existing database the null-write defect survived the upgrade *and* the optimizer stopped substituting the
  column, silently retiring every index built on it. `ColumnMatches` compared the column's type, character set
  and nullability — none of which change — so both were invisible.
  The entry framed detection as the hard half, and on its own terms it was: `information_schema` returns
  MySQL's canonical rendering of a generation expression rather than the text it was given, so comparing them
  compares against the server's formatter rather than against Koan. The answer was to stop parsing and start
  marking. A projected column is now created with `COMMENT 'koan-gen:<fingerprint>'`, the fingerprint being a
  hash of the expression the dialect produced, and validation compares a marker Koan wrote against one Koan
  computes — exact, with no normalizer to get wrong. A column with no marker was written by a Koan that did not
  know to leave one, which is exactly the population that needs rebuilding.
  Reported rather than made fatal: a stale projection still answers reads, so it surfaces as Degraded with the
  column named, and an operator can rebuild it. Making it corrective would stop every existing MySQL deployment
  from booting on upgrade with no remedy but manual DDL. **Repairing it automatically under DDL consent is the
  natural next step and is not done** — `EnsureCreatedAsync` adds absent columns but treats all drift as
  something to report or refuse, never to correct. That is carried as PMC-052.
  `RelationalColumnState.ProjectionStamp` is the one shared addition; everything else is MySQL-local, because
  MySQL remains the only store that compares definitions at all.

- **PMC-050 — the AOT proof re-runs now, on a schedule, and every cell was proven by breaking it.**
  Opened and closed on 2026-08-21. `scripts/aot-verify.ps1` publishes the `AotRelational` sample under
  ILC and **runs** the binary; `.github/workflows/aot-verify.yml` runs it daily on `windows-2022`.
  **Where.** A scheduled lane, not the certification ratchet. `scripts/green-ratchet.ps1` was the other
  candidate and was rejected for the reason the entry itself names: it is a deliberate manual boundary,
  and manual is exactly the status ARCH-0093's proof already had when it decayed for five weeks. A guard
  that waits for someone to decide to run it inherits the failure mode it exists to remove. Per-PR was
  rejected on cost (a publish is minutes), and compile-only was rejected on evidence — see below.
  **Breadth.** Six cells exist; two run daily. The daily pair is `Sqlite` and `SqlServerInvariant`,
  chosen because they need no container and because between them they catch all three PMC-049 defects.
  The four server cells (`Postgres`, `Cockroach`, `MySql`, `SqlServer`) run from the same script with
  `-Connectors All` where Docker is available. That split is forced, not preferred: every workflow in
  this repository runs on Linux, and a GitHub-hosted **Windows** runner — which win-x64 ILC requires —
  cannot host the Linux database containers those cells need. Dropping Cockroach was considered, since
  it shares both `Npgsql` and `NpgsqlRepository` with Postgres and adds no distinct ILC input; it was
  kept because the on-demand matrix is cheap and because ARCH-0093 and DATA-0120 now assert five
  backends, and a matrix that reproduces four of five invites exactly the inference this cycle keeps
  being burned by.
  **The compile-only proxy is dead, and the trial shows why.** Reintroducing `MetadataToken` on the
  mapping path produced a **successful publish** and a binary that exited 1 on the first entity it
  mapped. An ILC compile would have gone green on that change.
  **Every cell was proven against the defect it exists to catch**, red then green:
  reference manifest → publish fails (`DirectoryNotFoundException`); `MetadataToken` → publish succeeds,
  run exits 1; `Assembly.GetName()` on a satellite → `SqlServerInvariant` reports boot died on
  `CultureNotFoundException` before reaching the driver's refusal.
  Two findings came out of building it. The manifest defect fires only on a *first* RID publish, so a
  warm intermediate tree hides it — the script now removes the sample's `obj` so a developer machine and
  a fresh CI checkout mean the same thing. And the satellite defect is caught by **no** ordinary cell:
  SQLite ships no satellites, and the SqlServer build carries culture data because SqlClient demands it,
  which makes its satellites nameable. `SqlServerInvariant` exists solely to put satellites and invariant
  mode in one process; it asserts the refusal it should get and the failure it must not.
  **Same-PR half.** `scripts/aot-lint.ps1` runs as ratchet leg F, which the PR gate already invokes, and
  rejects `MetadataToken` and `(dynamic)` in `src/` — the two constructs whose damage is documented. Both
  rules were proven red-then-green, and the gate trial is the point: with `MetadataToken` reintroduced the
  ratchet's **build leg passes** and only leg F fails, which is precisely the change that would otherwise
  sit in `dev` until the next morning. It is a complement to the daily lane, not a substitute; a grep
  cannot enumerate what ILC forbids, which is why compile-only was rejected in the first place.
  **Known residual:** the four server cells are not machine-guarded, so a `Npgsql`, `MySqlConnector` or
  `Microsoft.Data.SqlClient` version bump could break server AOT and go unnoticed until someone runs the
  matrix. `docs/SURFACES.md` carries that distinction with its date rather than leaving it in prose.

- **PMC-049 — the single binary does reach the servers, and three framework defects stood between the
  claim and the proof.** Opened and closed on 2026-08-21. Measured, not reasoned: a minimal Koan console
  (`samples/fundamentals/AotRelational`) NativeAOT-published win-x64 once per connector and run against a
  real container, writing and reading one `Note` through the ordinary `Entity<T>` surface, with the row
  then confirmed in the store from outside the application.
  **All five relational backends publish and run**: SQLite (26 MB), MySQL 8.4 / MySqlConnector 2.6.1
  (28 MB), PostgreSQL 17 / Npgsql 10.0.3 (32 MB), CockroachDB v24.3 on the same `NpgsqlRepository`
  (32 MB), SQL Server 2022 / Microsoft.Data.SqlClient 7.0.2 (35 MB). No provider produced a link failure
  and none needed a trimming suppression; the IL2026/IL3050 warnings are the expected Newtonsoft and
  reflection-path family ARCH-0093 already documents.
  **The one real provider constraint is SQL Server's, and it is not about AOT.**
  `Microsoft.Data.SqlClient` refuses globalization-invariant mode outright —
  `System.NotSupportedException: Globalization Invariant Mode is not supported`, thrown from
  `SqlConnection.TryOpen`. A SQL Server build therefore carries culture data
  (`InvariantGlobalization=false`); the other four do not need it. That is a driver policy, so it applies
  to a JIT build in invariant mode just as much, and no AOT work can lift it.
  Getting there required three framework repairs, each a genuine defect on the AOT path:
  `WriteKoanReferenceManifest` wrote into the RID-specific intermediate directory without creating it, so
  the *first* `-r <rid>` publish of any Koan application died with `DirectoryNotFoundException`;
  `MemberInfo.MetadataToken`, which four sites used to recover declaration order, does not exist under
  ILC and threw `MappingCompilationException: There is no metadata token available for the given member`
  on the first entity mapped; and `AppBootstrapper`'s `AddAsm` called `Assembly.GetName()` on every
  assembly, which materializes the culture and threw `CultureNotFoundException` in invariant mode for the
  eleven satellite resource assemblies SqlClient ships — a Koan bug that presented as a SqlClient one.
  The second of those had also broken **SQLite**, which ARCH-0093 certified working: the mapping compiler
  landed 2026-08-06, three weeks after the 2026-07-17 proof, and nothing re-ran the proof. The single-binary
  claim had been false for the floor as well as unproven for the servers, and only publishing it showed that.

- **PMC-047 — the AOT-clean ADO surface had no caller, because the framework was paying for Dapper and using
  none of it.** Opened and closed on 2026-08-21. `Koan.Data.Relational/Ado` was written for ARCH-0093 so SQLite
  could avoid Dapper's runtime IL emit under NativeAOT, and then nothing adopted it — SQLite hand-rolled its
  commands and the three server adapters called Dapper directly.
  The question "should Dapper live behind one framework seam" turned out to have a better answer, and the
  evidence decided it rather than taste: every Dapper call in all three adapters was untyped (`QueryAsync`,
  `QuerySingleOrDefaultAsync`) or scalar, and each immediately cast the row to `IDictionary<string, object>` and
  handed a plain dictionary to `plan.Hydrate`. Dapper's compiled materializer — the whole reason it exists, and
  the exact thing NativeAOT forbids — was never called. It was serving as a dictionary reader and a parameter
  bag, both of which `AdoCommands` and `SqlParameters` already provided.
  So SQL Server, MySQL and PostgreSQL/CockroachDB now execute through that surface, the package is gone from
  the tree and from `Directory.Packages.props`, and the Postgres package description no longer advertises
  "Dapper-based SQL integration". `SqlParameters.FromObject` absorbs the raw-query binder each adapter had been
  carrying privately. The surface's documentation had also outlived a "Dapper-backed twin" that R11-02 retired;
  that is corrected.
  The consequence for DATA-0120 is the point: the collapse is over four adapters again, not three, because the
  reason SQLite had to be excluded no longer exists.

- **PMC-042 — the Jobs suites were not flaky; one spec depended on an unspecified order.** Opened and closed on
  2026-08-21. `retired_work_types_are_dead_lettered_without_blocking_valid_work` seeded a retired ledger row and
  a valid job at the same instant, leaving them tied on `(VisibleAt, FirstSubmittedAt)` — the claim window's
  entire sort. Which one the conveyor claimed first was therefore undefined, and it matters: `ClaimNextRunnable`
  dead-letters unregistered rows only until it reaches the first registered one, then returns. Claim the retired
  row first and the spec passes; claim the valid job first and the retired row is simply deferred to a later
  pass, which the spec read as a failure.
  It was filed as an isolation problem because that is what it looked like — it failed in a batch run and passed
  alone, on PostgreSQL and later on SQL Server. It was neither: the order was arbitrary and the arbitrary choice
  moved with load. Adding the identity tiebreaker (PMC-046) made it deterministic and deterministically the
  other way, which turned an intermittent failure on one store into a reproducible one on three — and that is
  what made it diagnosable at all.
  The spec now seeds the retired row a minute earlier, so the claim order is the one it is actually about:
  head-of-line blocking, a retired row the conveyor reaches *first* being settled and stepped over. The
  behaviour was never wrong. A retired row behind claimable work is dead-lettered on a later pass, and claimed
  rows leave the queued set, so it always eventually reaches the head.

- **PMC-046 — a paged read over a non-unique sort had no tiebreaker.** Opened and closed on 2026-08-21.
  DATA-0119 moved "the order a page is a window onto" to the framework, and closed the case where the caller
  named no order at all. Naming a sort is not naming a total order: paging by Status, where many rows share a
  Status, left the store free to break those ties differently per request, so page two repeated and skipped
  rows exactly as it would have unordered. `EnsureOrderForPage` now appends the entity identity to every
  paginated read — the caller's keys lead and keep their direction, the identity only settles rows they left
  equal, and it is skipped when the caller already ordered by identity. An unpaged read is untouched: with no
  window to be a window of, ties cost nothing and a second key would cost a sort.
  Found by reading, not by a failing test. Planning the DATA-0120 collapse meant reading `Order` across the
  four relational repositories; three were identical down to the comment and MySQL differed. The tempting
  conclusion was that MySQL had drifted. It had instead been appending the identity to every `ORDER BY` itself,
  and was the only store whose paged reads were stable — a majority-rule collapse would have deleted the fix
  and kept the bug on all four. MySQL's private copy is now gone, and `Order` is logically identical across the
  four, which is a concrete input to DATA-0120.
  `PageOrderOwnershipSpec` asserted the defect as intended behaviour — that a paged query with a caller sort
  reaches the adapter with exactly one sort spec — which is why nothing caught this. It now pins the decision
  in three parts and fails against a disabled fix.
  The risk in the change was that lengthening a sort would push an adapter off its native paging, and that
  turned out to be covered already: `SortPushdownConvergence.AssertNothingFallsBackAsync` pages by every
  portable scalar in both directions, so each of those queries now carries the appended identity and the
  guard fails if the store stops doing the work. It stayed green on all seven adapters that derive the
  oracle — SQLite, PostgreSQL, CockroachDB, SQL Server, MySQL, MongoDB and Couchbase.

- **PMC-038 — MySQL could not write a null.** Opened 2026-08-20, closed 2026-08-21. The entry recorded a
  seeding failure that blocked the shared filter corpus; the premise check found the corpus was not the subject.
  `MySqlDialect.Read` built every scalar as `CAST(JSON_UNQUOTE(JSON_EXTRACT(...)) AS SIGNED)`, and
  `JSON_UNQUOTE` renders a JSON null as the four-character string `null`. That expression is what a stored
  generated column is built from and what every filter and order emits, so an entity with a null nullable
  scalar could not be inserted at all — `Truncated incorrect INTEGER value: 'null'` — and no filter could be
  evaluated across one. Reading a JSON scalar now goes through `JSON_TYPE`, which separates a JSON null from a
  string that happens to read "null", so a real value spelled that way still round-trips. MySQL now runs the
  shared `FilterConvergence` oracle, which is the thing the register entry originally asked for and the thing
  that would have caught this years earlier. The defect had survived because MySQL's suite was the smallest of
  the relational set and nothing in it wrote a null. Existing tables keep the old expression: PMC-045.

- **PMC-041 — a declared index built nothing on four of the six stores that should build one.** Opened and
  closed on 2026-08-21. `[Index]` now becomes a real index on PostgreSQL, CockroachDB, SQL Server, MySQL and
  Couchbase, joining SQLite and MongoDB, and each store proves the planner will use what it built rather than
  only that it exists. The survey that scoped the work is the durable part: ElasticSearch and OpenSearch index
  every field through their own mapping, and Redis, JSON and InMemory have no secondary-index concept, so all
  five are correctly out of scope rather than merely unimplemented.
  Two shapes, both driven by what the store already holds. PostgreSQL and CockroachDB build an expression index
  from the dialect's own read — usable only because every cast that dialect emits (`boolean`, `bigint`,
  `numeric`) is immutable. SQL Server and MySQL index the persisted column they already compute per mapped
  scalar, which is the column their reads resolve through, so the optimizer substitutes it without the query
  naming either. Couchbase builds a secondary GSI over the same path grammar its filters use; its path spelling
  had to be lifted out of the generic document plan first, because an index built for a container and a filter
  compiled for an entity have to agree character for character or the query service ignores the index.
  The find that justified the whole slice came from asking what an index does to a text property: SQL Server
  built one over `nvarchar(4000)` and then rejected the first insert whose key exceeded 1700 bytes. Trading
  "the index does nothing" for "the index breaks writes" would have been strictly worse, and no existing spec
  covered it. Both stores now decline a key they cannot hold and record it as unproved; the deeper fix is
  PMC-043.

- **PMC-040 — the relational schema seam was registered and unused.** Opened 2026-08-20, closed 2026-08-21.
  All four relational runtimes now route schema work through `RelationalSchemaOrchestrator`, and the four
  hand-rolled `*Schema.cs` files (SQLite 201, MySQL 326, SQL Server 140, Npgsql 103) are deleted.
  The two questions the entry asked were answered by the tree rather than by argument. **Connections:** three
  of the four already received an open connection from their repository, so the executor takes one and lives
  for a single schema operation; only SQLite differs, because opening a managed SQLite connection *creates the
  database*, so its executor opens non-creating to read and creating to write. That distinction was not
  cosmetic — the first adoption made SQLite materialize its file before the consent gate, and
  `NoDdl_remains_an_explicit_non_creating_policy` had been passing without ever reaching the assertion that
  would have caught it, because a message-wildcard assertion failed first. **Column sets:** the grouping
  matched (all four group bindings by `PhysicalPath.Name`), but the plan could not express two things the
  adapters built — SQLite's `INTEGER PRIMARY KEY AUTOINCREMENT`, which reconstructs from a single
  `IsIdentity` column that is also `IsGenerated`, and the persisted computed columns SQL Server and MySQL
  create per mapped scalar, which had to become a framework decision the store merely consents to.
  Adoption also removed a second mapping compiler (`RelationalCompatibilityMapping`, reached only through the
  type-based orchestrator entry) that would have validated a shape no adapter reads or writes, and four
  executor members that existed only as defaults feeding other members. `NativeTypeFor`, added the previous
  day to make adoption lossless, turned out to be the wrong half of the answer and was replaced by
  `ColumnMatches`: rendering the expectation into the store's vocabulary still left the framework comparing
  spellings it cannot judge. Details and the full seam rationale are in DATA-0119's implementation section.

- **PMC-039 — sparse Entity projection removed rather than pushed down.** Opened and closed on 2026-08-20.
  The question was why no adapter pushed a projection down. The answer was that the feature should not have
  been in the data layer at all.
  `Data<T>` returns `TEntity`, so a partial result still had to *be* an entity. The fallback cloned the
  materialized row and assigned `default` to every unselected property, which meant: no IO or transfer was
  saved, since the row was read in full and then blanked; the result lied, because `0` and `0001-01-01` are
  indistinguishable from real values with nothing marking them absent; it violated the entity's own
  invariants, blanking a `List<T>` that its declaration guarantees is never null; and because the Id was
  deliberately preserved, saving such an entity wrote the blanks back. Measured on SQLite: a widget projected
  to `Name` came back `Sequence=0 ObservedAt=0001-01-01 Sightings=NULL`, and one `Save()` later the stored row
  had lost all three, permanently and silently.
  Nothing in the tree constructed one — no web surface parsed a field list, no Entity static built one, and
  the only caller was a Data.Core spec. Two mechanisms already cover the intent honestly: `RecordSet` for a
  partial data shape (which the fallback's own error message pointed at) and `IProjectionOf<TEntity, TSummary>`
  summary DTOs, plus `RowProjection` for field visibility, for a response shape.
  So `Projection`, `QueryDefinition.WithProjection`, `InMemoryEntityProjection`,
  `RepositoryQueryResult.ProjectionHandled` and `QueryReceiptAxis.Projection` are gone, along with the
  hardcoded `ProjectionHandled = false` in all seven adapters and a dead projection branch in
  `RelationalCommandPlanner.Query`. This is a compile-level break for an external adapter that set that
  property — deliberately so; the alternative was leaving a published surface whose only behaviour was to
  corrupt data. Sparse fieldsets, if they return, belong on a shape that can express absence.


- **PMC-036 — every store computes a collection order key itself.** Opened and closed on 2026-08-20.
  `?sort=-Sightings.LastChangedAt` — "by each widget's latest sighting" — is an aggregate over a nested array.
  No adapter pushed it down: the mapping compiler stops at a collection of objects, because such a collection
  has no single queryable physical path, so every relational runtime declined the key and MongoDB broke out of
  its sort loop. Answers stayed correct because `InMemorySorter` finished the ordering, which meant
  materializing the whole result and giving up native paging for a query written in one line.
  Verified before the fix rather than assumed: the SQLite repository's own receipt reported `handled=0/1` for
  the collection key and `1/1` for a scalar one.
  Every store Koan ships on can express it, and now does — `ARRAY_MAX` over an array comprehension (Couchbase),
  `jsonb_array_elements` (PostgreSQL, CockroachDB), `OPENJSON` (SQL Server), `json_each` (SQLite), `JSON_TABLE`
  (MySQL), and `$max` over a `$map` (MongoDB, which cannot sort by an expression through `find` at all, so the
  query runs as a pipeline that adds the value, sorts and pages on the server, and removes it again).
  One rule decides it for the whole relational family — `RelationalCollectionOrder` locates the array inside the
  document the root binding owns — and one dialect member expresses it, so there are four grammars rather than
  four algorithms. A dialect that cannot express it declines the ordering rather than refusing the query, and
  then does not paginate either: a page is only a page of an ordered set.
  Proved by `SortPushdownConvergence`, a cross-adapter oracle that asserts both halves — the receipt claims the
  key, and the ordering equals the one the in-memory sorter would have produced. Its corpus is adversarial and
  total: several sightings per widget so taking the first element diverges, indices of 3 and 20 and 30 and 40 so
  comparing extracted JSON as text diverges, one widget with no sightings so the NULL aggregate must land where
  the framework puts it, and no two widgets sharing a value so no assertion rests on a database promising an
  order among equals. PostgreSQL needed its null placement stated, since it sorts NULL as larger than every
  value — the opposite of the framework's sorter.
  The oracle also surfaced a defect of its own: Couchbase issued `CREATE PRIMARY INDEX` against a collection the
  query service had not seen yet, so the *first* query against a newly created collection could fail with
  keyspace-not-found. Creating the collection is answered by the data service and the statement by the query
  service, and the cluster map reaches the second a moment later. That wait is now bounded and explicit.


- **PMC-020 (first half) — durable test evidence.** Delivered on 2026-08-20. Every project in the
  ratchet's test leg now emits a TRX, and `scripts/aggregate-test-evidence.ps1` merges the run records
  and those TRX files into one `artifacts/ratchet/test-manifest.json`: per-project and aggregate
  total/passed/failed/skipped, wall duration, lane, exit code, and `startedUtc` so completion order is
  recoverable from a parallel wave. It is written **before** the verdict, so a red run keeps its
  evidence, and it never changes pass/fail — the ratchet still decides from process exit codes.
  A project that produced no readable TRX is named as an evidence gap rather than left as a silent hole
  in the totals, which is what a host killed by `--blame-hang-timeout` looks like.
  The aggregator is a separate script precisely so its failure paths could be proved: crafted inputs
  covering a passing project, a failing one, skips, a hung project with no TRX, and a corrupt TRX
  produce total=49/passed=43/failed=3/skipped=3 with the failed and unreported projects named, and the
  aggregation still exits 0. End-to-end proof came from a real RED run — 113 projects, 4998 tests,
  4959 passed, 17 failed, 22 skipped, 13 projects named, 0 unreported. The remaining CI-parity half
  stays open under PMC-020.

- **PMC-005 / PMC-018 / PMC-027 — premises verified against the tree and found lapsed.** Closed on
  2026-08-20 by a sweep of the whole open register rather than by new work.
  **PMC-005** (release-tooling worktree discovery): `Koan.Packaging.FindRepositoryRoot` already accepts
  both forms — `Directory.Exists(gitPath) || File.Exists(gitPath)` — so the `.git` indirection file used
  by linked worktrees resolves. Exercised incidentally the same day: two linked worktrees were created
  and tooling ran from the repository without incident.
  **PMC-018** (remote providers pre-resolving generic `ConnectionStrings:Default`): a whole-tree search
  finds exactly one reference, in SQLite's constants, and the entry itself records that SQLite no longer
  has the ambiguity. None of the five named remote providers — PostgreSQL, SQL Server, CockroachDB,
  MongoDB, Couchbase — carries it.
  **PMC-027** (`Koan.Storage.Connector.Local.Tests` unmarked and non-compiling): the project declares
  `Microsoft.NET.Test.Sdk`, is discovered by the ratchet, builds, and passes 31/31.

- **PMC-013 — Web Backup graduation.** Closed on 2026-08-20; every premise had lapsed. The entry
  described Web operation tracking, a cancel endpoint that marks tracker state without stopping I/O,
  sequential global export despite `MaxConcurrency`, and a missing Web suite. **`Koan.Web.Backup` was
  retired** in `33a50be67` ("retire unsupported backup projection"), which is the graduation decision
  the entry asked for, taken in the honest direction: the unsupported projection was removed rather
  than polished. No source file, solution entry, or package remains; only untracked build leftovers.
  The separate "complete ZIP is memory-resident" claim also lapsed — `a39edffa4` moved archive creation
  onto a temporary `FileStream`, and zero `MemoryStream` uses remain in `Koan.Data.Backup`. What
  survives is `Sylin.Koan.Data.Backup` alone, still `unassessed` in the product surface, whose read
  path is now governed by DATA-0113. Any future Web projection is a new decision, not this entry.

- **PMC-014 — Data Backup metadata-only encryption flags.** Closed on 2026-08-20; the entry was stale.
  `Encrypt` and `EncryptByDefault` were removed in `a39edffa4` (graduate backup and soft-delete
  leaves), so the footgun the entry described — a flag named `Encrypt` that records manifest intent and
  encrypts nothing — no longer exists. Verified by inspection rather than by the register: `BackupRequest`
  exposes only `StorageProfile`, `Partition`, and `PageSize`; `BackupArchiveManifest` carries no
  encryption field; no encryption claim survives in the reference, guide, or recipe surfaces; and
  `TECHNICAL.md` states the boundary positively — the package "does not own … encryption". The register's
  own choice ("remove/rename the metadata-only flags, or specify authenticated encryption") was taken in
  the first direction, and archive encryption remains an unclaimed capability rather than a half-built one.

- **PMC-003 / PMC-028 / PMC-032 — warning and connector-test graph premises.** Closed by current
  evidence in R12-02 on 2026-07-19. R11-07's exact public-release solution build reports zero warnings
  and errors, superseding PMC-003's 19-warning baseline. SQLite's discovery fake implements
  `ResolveServiceIntent`, and its full connector-owned Release suite passes 36/36. A fresh XML-based
  inventory of every test-project `ProjectReference` finds zero missing targets and no retired
  `Koan.Core.Adapters.csproj` reference. No suppression, replacement dependency, or compatibility fake
  was added for defects that no longer exist.

- **PMC-024 — direct-reference build-fixture isolation.** Resolved in R12-02 on 2026-07-19. The
  fixture no longer restores `src/Koan.Core` under a throwaway NuGet configuration. A synthetic
  `Koan.Core` project, package feed, global package cache, intermediates, outputs, and every evaluated
  `ProjectReference` now live under one temporary root; only the real composition targets are imported
  read-only. The focused fixture passes 1/1 offline, preserves package/project direct-reference truth,
  asserts graph containment, and proves a planted missing package fails inside the temporary root.
  No asset backup/restore, repository build lock, or production helper was added.

- **PMC-007 / PMC-015 — Web filtering and portable Entity names.** Closed in R12-02 on
  2026-07-19. PMC-007's unsafe-degradation premise was stale: Web parses one filter AST, Data pushes
  only declared nodes and evaluates the residual before sort/pagination, and malformed, unknown, or
  unsupported input maps to 400. The shared HTTP surface now pins compound operators, mixed-case
  field binding, and malformed/unknown anti-drop behavior across InMemory 74/74, JSON 52/52, and
  SQLite 52/52. PMC-015 is repaired at Data's existing cached first-use shape guard: public Entity
  properties that differ only by case reject with both names and one rename correction before adapter
  creation. Exact guard/activation tests pass 9/9 and affected builds are warning-clean. No provider
  flag, model annotation, persisted-name mapping, or new filter layer was added.

- **PMC-002 / PMC-004 — MCP host transport and application JSON contract.** Resolved locally in
  R12-02 on 2026-07-19 as a deliberate pre-0.20 break. The transitional HTTP master/nullable
  override, SSE-derived primary names, and per-Entity transport metadata are removed. Hosts now own
  three explicit edges (`EnableStdioTransport`, `EnableStreamableHttpTransport`, and deprecated
  `EnableLegacySseTransport`), one `HttpRoute`, and unified session limits. Entity and custom-tool
  payloads, schemas, deltas, binding, and Code Mode share one camelCase `[McpIgnore]`-aware contract;
  protocol DTO names remain spec-owned. Conformance passes 80/80, Streamable/legacy HTTP 19/19,
  field exclusion 5/5, Code Mode 27/27, source FirstUse/GoldenJourney 3/3, bootstrap pillars 13/13,
  and the MCP Release build is warning-clean. Public docs pass 233/42. No aliases preserve the
  misleading pre-preview vocabulary.

- **PMC-001 — Entity / Jobs metric collision.** Resolved locally in R12-02 on 2026-07-19. The
  `JobMetric` Entity was framework-owned persistence accidentally exposed as application language;
  users were only taught its summary operation. The row is now internal with its CLR type, storage
  identity, and `Count` field unchanged. One public `JobMetrics.Summary(...)` concept owns retained
  throughput intent. Jobs passes 84/84, Tenancy 16/16, and the Jobs Release build is warning-clean.
  No alias, field migration, model attribute, or analyzer assembly was added.

- **PMC-025 — Windows first use.** Closed as a stale historical premise in R12-02 on 2026-07-19.
  The current `ApplicationProbeHost` supplies no EventLog override, the exact R08-05 Windows candidate
  passed package-only FirstUse, R11-07 passed the same public contract, and the current source
  FirstUse proof passes 1/1. The repository's Microsoft.Extensions.Logging.EventLog 10.0.8 runtime
  disables the EventLog sink after a `SecurityException` while other providers continue. Koan keeps
  standard ASP.NET Core provider ownership rather than adding a parallel logging switch or removing
  application-selected providers for a defect that no longer reproduces.

- **PMC-033 — Storage layered activation.** Resolved locally in R12-02 on 2026-07-19. The historical
  GardenCoop C2 trigger had already disappeared with its obsolete Storage dependency and now passes
  1/1. The remaining invariant was repaired at the Storage chokepoint: a reference makes Storage
  available; a declared profile/default or actual service use activates its one routing plan.
  Profile configuration still validates at startup, unconfigured service use still fails with the
  existing correction, and facts distinguish inactive availability from selected routes. Media Web
  passes 8/8, Storage 20/20, bootstrap pillars 13/13, and Data.AI 87/87 without sample-only profiles.

- **PMC-031 — Tenancy / Local Storage test composition.** Resolved locally on 2026-07-18. The shared Tenancy runtime
  fixture now supplies an isolated temporary Local Storage profile because its test graph intentionally references the
  functional Local connector. The two formerly blocked Cache/Tenancy contracts reach their assertions and the full
  focused Tenancy suite passes 87/87 without weakening Local's missing-path validation. The separate question of
  transitive unused Storage activation was later resolved by PMC-033.

- **PMC-010 — public module catalog.** Promoted into
  [R08-03](work-items/r08/R08-03-canonical-product-surface.md) and resolved locally on 2026-07-17.
  The stale hand-maintained catalog is retired. One compiler now joins 108 evaluated standard
  package/project facts to 14 explicit evidence claims and generates both human and machine references.
  Thirty-seven missing package-owned READMEs and 88 unassessed packages remain visible rather than being
  converted into support claims.
- **PMC-019 — connector logging security.** Promoted into
  [R08-02](work-items/r08/R08-02-safe-connector-telemetry.md) and resolved locally on 2026-07-17.
  `Redaction` owns one credential grammar, `KoanLog` sanitizes structured connector context once, shared
  configuration/discovery/orchestration chokepoints own narration, and a repository policy rejects direct
  logger bypasses in the bounded source surface. Runtime mutation proof passes 28/28, the policy passes 1/1,
  and 17 affected connector projects build. The exact non-claim excludes arbitrary application, driver,
  third-party, and business-payload logs.
- **PMC-016 — exact cross-event release recovery.** Promoted into
  [R08-01](work-items/r08/R08-01-durable-release-wave.md) and resolved locally on 2026-07-16. One
  hash-bound GitHub Release escrow retains the exact nupkg/snupkg and evidence bytes; the resumable
  coordinator reconciles an incomplete prior wave before later source, replays symbols, and refuses
  public identity without exact prepared custody. Failure-injection and workflow-contract evidence is
  recorded in R08-01. Real immutable-Release observation remains an operator-gated R08 step.
- **PMC-017 — release input lineage.** Promoted into
  [R08-01](work-items/r08/R08-01-durable-release-wave.md) and resolved on 2026-07-16. Lineage schema
  3 persists normalized per-package evaluated input maps; compiler and planner independently compare
  prior and current ownership. Real Git add/change/rename/delete scenarios select only the owner,
  while missing, noncanonical, or tampered maps fail closed.
- **PMC-012 — layered service discovery.** Resolved on 2026-07-15 through ARCH-0114's uniform
  adapter-declaration and engine-activation contract. Core Unit passes 112/112, Mongo 70/70, and the
  prior Couchbase 17/17 proof is corroborated by the final clean aggregate completing without the
  earlier node-readiness failure. No adapter-specific election fork remains.
- **PMC-011 — automatic reverse-dependent release closure.** Promoted into
  [R07-03](work-items/r07/R07-03-automatic-package-lineage.md) and resolved on 2026-07-15. The
  evaluated graph, durable Git lineage, complete breaking closure, and independent leaf behavior are
  recorded in that card and in [PROGRESS.md](PROGRESS.md). This history remains here so the original
  release-safety concern cannot disappear when the active register changes.

## Working order

Before the first real automatic publication, compile one evidence-derived public product boundary;
PMC-019 and PMC-016 are already resolved locally, while release recovery still awaits real observation.
Then start with the inventory-oriented items (`PMC-003`, `PMC-007`, `PMC-009`, `PMC-018`), because they establish
the real size and severity of the work. Discuss
compatibility-sensitive API choices (`PMC-001`, `PMC-002`, `PMC-004`, `PMC-008`, `PMC-014`,
`PMC-015`, `PMC-022`, `PMC-023`) and the bounded Backup graduation decision (`PMC-013`) next. Finish with independently
useful release-tooling polish (`PMC-005`, `PMC-006`, `PMC-020`). Fewer cards may result if one root repair
responsibly closes several entries.

## Closure rule

Remove an entry only when its decision and evidence are linked from `PROGRESS.md`, or when a recorded
review rejects the work as unnecessary. Do not mark an item complete merely because its warning was
suppressed, its documentation was softened, or the active cycle ended.
