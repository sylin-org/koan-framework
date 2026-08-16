# Koan Skill Forward-Evaluation Rubric

## Evaluation boundary

This rubric scores model responses from realistic forward tests. The repository's static validator
checks corpus structure and coverage; it does not invoke an agent, inspect a response, or model-judge quality.

A third check sits between them: `scripts/skills-verify.ps1` compiles the skill's own claims against
the published packages, so an identifier or API the skill names but that does not exist fails without
any model being run. It answers "is the guidance true?", not "did the agent do well?" — score the
latter here.

The evaluation asks one question: did the skill make a developer more successful while making Koan
feel simpler, more expressive, and more composable?

## Procedure

1. Start each run from the same repository state and user prompt.
2. Run once with the selected skill and once without it.
3. Give neither run the expected answer or behavior IDs.
4. Record the response, edits, commands, elapsed time, and user intervention.
5. Apply the routing gate, hard failures, and weighted score below.
6. Prefer a smaller successful response over a longer response with the same result.

## Routing gate

- Build, extend, fix, test, research, provider-change, and ship work routes to `koan`.
- Strictly read-only explanation routes to `koan-explain`.
- Koan framework migration routes to `koan-upgrade`.
- Unrelated .NET work and non-software koans activate none of the three.
- A provider change is not a framework upgrade.

A wrong route is a failed case even when the technical answer is plausible.

## Behavior catalog

### Required behaviors

### `R-OUTCOME-FIRST` — Start from the developer's sentence

Lead with the useful application outcome, then translate it into Koan pieces.

### `R-KOAN-GRAMMAR` — Use recognizable Koan language

Prefer Reference = Intent, one `AddKoan()`, `Entity<T>`, expressive Entity operations,
`EntityContext` switches, and `EntityController<T>` over generic service/repository ceremony.

### `R-SEMANTIC-STACK` — Show the stack as meaning

Name what each selected piece contributes to the user's story, not merely package names.

### `R-SMALLEST-STACK` — Choose only earned pieces

Recommend the fewest capabilities and prerequisites that honestly satisfy the outcome.

### `R-GROWTH-PATH` — Make the next pieces obvious

Show concise Now, Later, and Preserved guidance without pre-installing future capabilities.

### `R-ONE-QUESTION` — Spend one question only when consequential

Inspect first and ask at most one question when data, security, public contracts, or topology truly
depend on the answer.

### `R-BREADTH-ROUTING` — Consider Koan's full capability vocabulary

Route naturally across data adapters, Web, identity and tenancy, Jobs, communication, cache,
storage, media, AI, vectors, MCP, Canon, testing, and operations while loading only what is relevant.

### `R-SAME-OPERATIONS` — Compose doorways over one application

Keep HTTP, MCP, jobs, and other projections on the same domain operations, authorization, and tenant
rules instead of creating parallel business logic.

### `R-CURRENT-EVIDENCE` — Verify unstable or exact details at use time

Use current first-party documentation, package metadata, source, facts, and tests proportionally.
Do not narrate compatibility governance when it is irrelevant to the developer's outcome.

### `R-EXTERNAL-RESEARCH` — Research an earned external boundary

For changing standards or vendor behavior, use current primary sources, cite the applicable result,
and distinguish source claims from inference.

### `R-DIAGNOSE-FIRST` — Establish the cause before repair

Trace the failed guarantee and effective composition before changing code or configuration.

### `R-SMALLEST-DELTA` — Preserve the application's architecture

Change only the owning expression and its earned proof; do not create a second architecture.

### `R-PROOF-BEHAVIOR` — Prove the user journey

Exercise the intended behavior through its meaningful public or application boundary.

### `R-PROOF-COMPOSITION` — Prove which pieces won

Show the effective provider, route, source, capability, or projection rather than inferring it from
a reference or successful build.

### `R-PROOF-CORRECTION` — Prove a useful failure

Exercise a missing, invalid, denied, or unavailable dependency path and verify corrective behavior.

### `R-SHIP-BOUNDARY` — Verify the declared deployment boundary

Preserve routes, data, security, readiness, and topology while reporting anything not proved.

### `R-CUTOVER-CHECKPOINT` — Keep a provider switch reversible

Separate route selection from data movement and record a checkpoint before changing the active
provider.

### `R-READ-ONLY` — Perform no mutation

Do not edit files, restore/build, start services, or change external state during explanation.

### `R-EVIDENCE-BOUNDARIES` — Separate observation, inference, and unknown

For every important conclusion, say what was observed, what it means, and what remains unknown.

### `R-SEMANTIC-MAP` — Explain the application in business terms

Connect Entities, operations, doorways, selected providers, guarantees, and failures into one
readable map.

### `R-EXPLICIT-TRANSITION` — Announce a trust-boundary change

Finish read-only explanation before explicitly transitioning to `koan` or `koan-upgrade` for a
requested mutation.

### `R-UPGRADE-BOUNDARY` — Preserve contracts, not obsolete ceremony

Name the source and target framework contracts, preserved routes/payloads/data/security/topology,
and the exact migration scope.

### `R-MIGRATION-LEDGER` — Make migration auditable and reversible

Record each obsolete expression, verified replacement, preserved contract, proof, and rollback.

### `R-STOP-UNKNOWN-TARGET` — Stop rather than invent a replacement

Report the exact evidence gap and leave that seam unchanged when no current replacement can be
verified.

### `R-NO-KOAN-ACTIVATION` — Leave unrelated requests alone

Do not activate a Koan skill for generic .NET work or the philosophical use of “koan.”

### Forbidden behaviors

### `F-WRONG-SKILL` — Route to the wrong public skill

### `F-GENERIC-DOTNET` — Hide Koan behind generic architecture

Introduce repositories, service layers, manual endpoint plumbing, or registration ceremony where
Koan's semantic pieces already express the outcome.

### `F-INTERNAL-RECIPE-AS-SKILL` — Expose internal recipes as public choices

Ask the developer to choose `koan-build`, `koan-auth`, `koan-ai`, or another internal sub-skill.

### `F-RELEASE-CEREMONY` — Burden the task with release bookkeeping

Lead with version pins, tags, commit hashes, compatibility ranges, registries, or release validation
that the requested outcome did not require.

### `F-PROCESS-DUMP` — Narrate framework development process

Dump claims, maturity classifications, package inventories, loader history, initiatives, or internal
publication gates into application guidance.

### `F-UNRELATED-CAPABILITIES` — Add speculative pieces

Reference, configure, or scaffold capabilities that are not required by the requested slice.

### `F-SILENT-FALLBACK` — Quietly weaken the named requirement

Substitute an in-memory or local provider after the requested provider fails without making that a
declared application policy.

### `F-BUILD-ONLY-PROOF` — Treat compilation as completion

Restore/build success alone does not prove behavior, composition, correction, or deployment.

### `F-CONTRACT-DRIFT` — Change an unrequested public contract

Alter routes, payloads, data meaning, authorization, tenancy, names, or topology without authority.

### `F-MUTATION-READ-ONLY` — Change state during explanation

### `F-PROVIDER-AS-UPGRADE` — Route a provider change to framework migration

### `F-DATA-MIGRATION` — Move or delete persisted data without separate authority

### `F-MUTATION-UNKNOWN-TARGET` — Change a seam whose replacement is unverified

### `F-INVENTED-API` — Guess a Koan API, package, default, or guarantee

### `F-SECONDARY-AS-AUTHORITY` — Use copied or secondary guidance over primary evidence

### `F-KOAN-ACTIVATION` — Activate a Koan skill for an unrelated request

## Scoring

Score only after the routing gate and hard failures pass.

| Dimension | Weight | Full-credit signal |
|---|---:|---|
| Outcome success | 35 | The requested journey works and its important contract is preserved. |
| Koan delight | 30 | The answer is concise, recognizably Koan, Lego-like, and leaves a clear growth path. |
| Truth and proof | 20 | Exact claims are evidenced; behavior, composition, and correction are proved proportionally. |
| Trust and scope | 15 | Read-only, migration, destructive, security, and external boundaries are honored. |

Hard failures include a wrong route, mutation during read-only work, invented APIs, silent fallback,
unauthorized data movement, or exposing release/process bookkeeping as the developer experience.

## Reporting

For each case record:

- route and pass/fail;
- required and forbidden behavior evidence;
- weighted score;
- commands, edits, elapsed time, tokens, and user interventions;
- comparison with the no-skill baseline; and
- the smallest skill change suggested by any failure.

Report static corpus validation separately from executed model evaluation.
