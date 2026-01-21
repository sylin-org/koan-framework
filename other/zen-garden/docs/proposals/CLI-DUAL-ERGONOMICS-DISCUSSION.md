# CLI Dual-Ergonomics Design Discussion

**Date**: 2026-01-21
**Status**: Draft - Design Discussion
**Foundation**: [CLI-API-SURFACE-ANALYSIS.md](../CLI-API-SURFACE-ANALYSIS.md)

---

## Executive Summary

This document captures a comprehensive multi-disciplinary discussion on designing Zen Garden's dual-ergonomics CLI system. The goal: create two complete, mirrored command interfaces—one optimized for human joy and clarity (Zen), one optimized for script precision and standardization (Normative).

**Core Requirement**: Perfect 1:1 mirroring between syntaxes, allowing seamless translation.

---

## Participants

### Dr. Sarah Chen - Semiotics & Cultural Linguistics
*"Symbols carry weight. The choice between 'offer' and 'create' isn't just vocabulary—it's philosophy made manifest."*

**Focus**: Meaning-making, cultural resonance, symbolic consistency

### Marcus Rodriguez - DevOps Engineering
*"Scripts fail at 3 AM. I need unambiguous syntax that won't break when someone adds a service named 'at' or 'this'."*

**Focus**: Scripting reliability, parsing safety, tooling integration

### Dr. Yuki Tanaka - Cognitive Psychology & UX
*"Working memory holds 7±2 chunks. Zen syntax should feel like natural language. Normative should minimize cognitive load through predictability."*

**Focus**: Human factors, learnability, error prevention

### Alex Thompson - CLI Design & Tooling
*"Look at kubectl, docker, git. Users expect certain patterns. Break them intentionally, not accidentally."*

**Focus**: Industry standards, autocomplete, help systems

### Priya Sharma - Semantic Architecture
*"Every verb must have clear boundaries. 'offer' could mean list, install, or inspect. Context ambiguity is semantic debt."*

**Focus**: Semantic clarity, verb taxonomy, context resolution

### James O'Connor - Technical Writing & Documentation
*"If I can't document it in one sentence, it's too complex. Both syntaxes must be teachable."*

**Focus**: Explainability, progressive disclosure, error messages

---

## Part 1: Foundational Principles

### Establishing the "Why"

**Sarah Chen**: Let's start with first principles. Why do we need two syntaxes? Most CLIs pick one philosophy and stick with it.

**Marcus Rodriguez**: Because we're serving two fundamentally different users. When I'm writing automation at scale—managing 50 stones, 200 services—I need something that won't surprise me. Reserved words, clear delimiters, no positional magic. But when I'm debugging interactively at 2 AM, fighting with `garden-rake services update --service-name mongodb --at http://stone-03:7185 --config-file /tmp/fix.yml` makes me want to cry.

**Yuki Tanaka**: The psychology here is fascinating. Interactive use is high-context, high-emotion. You're in flow state, problem-solving. Natural language syntax reduces friction. But scripting is low-context, low-emotion. You're writing once, running thousands of times. Explicitness reduces bugs.

**Alex Thompson**: Git actually tried to solve this with porcelain vs plumbing commands. Porcelain (`git commit`) is human-friendly. Plumbing (`git hash-object`, `git update-ref`) is script-friendly. But they didn't make them mirror each other—they're different conceptual layers. We're trying something harder: same operations, two interfaces.

**Priya Sharma**: The key insight is **semantic equivalence with syntactic divergence**. Every command must have identical semantics—same state changes, same effects—but optimize surface syntax for different cognitive models.

**James O'Connor**: And documentation becomes critical. Users need to understand both syntaxes exist, when to use each, and how to translate between them. That's a teaching challenge.

**Sarah Chen**: There's a deeper philosophical question: does syntax shape thought? Will zen users and normative users think differently about their infrastructure?

**Marcus Rodriguez**: I hope so. When I'm scripting, I want to think in state machines and idempotency. When I'm exploring, I want to think in gardens and offerings. Different tools for different mindsets.

**CONSENSUS**: Dual syntax is justified by serving distinct cognitive modes (interactive exploration vs. automated scripting), not just different user preferences.

---

## Part 2: Zen Syntax - Philosophy & Refinement

### Current State Assessment

**Alex Thompson**: Current zen syntax uses verbs like `offer`, `rest`, `wake`, `observe`, `watch`, `tend`, `place`, `invite`, `lift`, `make`. These are evocative but sometimes ambiguous. `offer` can mean list, install, or inspect depending on arguments.

**Sarah Chen**: Let's analyze the metaphor system. We have:
- **Lifecycle**: offer (give), rest (sleep), wake (rouse), nourish (feed)
- **Observation**: observe (look), watch (monitor)
- **Spatial**: place (position), lift (remove), tend (care for location)
- **Social**: invite (welcome)
- **Transformation**: make (change state)

The metaphor is consistent: we're tending a physical garden with living services. Beautiful. But does it scale?

**Priya Sharma**: I'm concerned about verb overloading. `offer mongodb` installs. `offer` lists. `offer mongodb info` inspects. That's three semantic operations on one verb. Normative syntax would split these: `services create`, `services list`, `services show`.

**Yuki Tanaka**: But that's actually cognitively efficient for humans. Context disambiguates. When I say "offer" with no arguments, you know I mean "show me what I can offer." With an argument, you know I'm offering something specific. It follows natural language pragmatics.

**Marcus Rodriguez**: Until someone names their service "info" or has a service catalog with an entry called "at". Then `garden-rake offer at info` is ambiguous. Is it "offer the 'at' service and show info" or "offer something targeted at stone 'info'"?

**Alex Thompson**: That's a parsing problem, not a conceptual problem. We can establish precedence rules. But Marcus has a point—zen syntax trades some precision for fluency.

**James O'Connor**: Let's document the zen principles explicitly:

### Zen Syntax Principles

1. **Natural Language Flow**: Commands should read like imperative sentences
   - Good: `garden-rake offer mongodb at stone-02`
   - Bad: `garden-rake --stone stone-02 offer --service mongodb`

2. **Positional Semantics**: Position conveys meaning
   - `offer mongodb` - install mongodb
   - `offer mongodb info` - inspect mongodb
   - `offer` - list offerings

3. **Metaphorical Consistency**: All verbs draw from garden/care metaphors
   - Lifecycle: offer, rest, wake, nourish
   - Space: place, lift, tend
   - Observation: observe, watch
   - Social: invite

4. **Context Over Explicitness**: Human interpretation fills gaps
   - `tend stone-02` implies "set tending context to stone-02"
   - `rest mongodb` implies "stop the mongodb service"

5. **Quiet Joy**: Commands should spark recognition and delight
   - "I'm going to rest the database for maintenance" feels intentional
   - "I'm going to stop the mongodb service" feels mechanical

**Sarah Chen**: I want to push on #5. "Quiet joy" isn't frivolous—it's about building a relationship with your infrastructure. When you `tend` a stone, you're acknowledging care. When you `invite` a stone to the pond, you're acknowledging community. This is infrastructure-as-garden, not infrastructure-as-machine.

**Marcus Rodriguez**: I respect that. But when I'm automating, I don't want poetry—I want reliability. That's why we need normative.

---

### Proposed Zen Refinements

**Priya Sharma**: Let me propose some refinements to reduce ambiguity:

| Current | Issue | Proposed | Rationale |
|---------|-------|----------|-----------|
| `offer` | Overloaded (list/install/inspect) | Keep as-is, add subcommands | Natural language allows context-dependent meaning |
| `observe` | Garden-wide vs stone-specific unclear | `observe` (garden) vs `status` (stone) | Spatial metaphor: observe = panoramic, status = specific |
| `make stone sing` | Non-obvious "sing" = verbose | Keep, add help | Poetic language justified by cultural resonance |
| `lift stone <name>` | "lift" unintuitive for "untrust" | Consider `release stone <name>` | "release" implies letting go of trust |

**Yuki Tanaka**: I disagree on `lift` → `release`. "Lift" implies removing something placed. "Lift the stone from the pond" is spatially coherent. "Release" implies freeing, which has different connotations—release could mean "stop controlling" vs "remove from pond."

**Sarah Chen**: Agreed. "Lift" matches "place". Spatial symmetry is important for mental models.

**Alex Thompson**: What about missing verbs? Analysis shows we need: `explore` (discover stones), `nourish` (update service), `touch` (health check), `garden` (orchestrate). Do these fit the metaphor?

**Sarah Chen**:
- `explore` - Yes. "Explore the garden to find stones."
- `nourish` - Yes. "Nourish the mongodb service with new config."
- `touch` - Maybe. "Touch stone-02 to check vitality." Could also be `check`.
- `garden` - Risky. As a verb, "garden" is too meta. Better: `harmonize` or `cultivate`.

**James O'Connor**: Let's keep it simple. `explore`, `nourish`, `check`, and `cultivate` would work. But do we need `cultivate`? What's the semantic difference from `tend`?

**Priya Sharma**: `tend` sets context. `cultivate` would be multi-stone orchestration—"cultivate the garden to install mongodb on stones that can handle it." Different semantics.

**CONSENSUS**: Zen syntax should prioritize metaphorical consistency and natural language flow. Some ambiguity is acceptable because humans resolve it via context. Proposed additions: `explore`, `nourish`, `check`, `cultivate`.

---

## Part 3: Normative Syntax - Precision & Standards

### Design Philosophy

**Marcus Rodriguez**: Normative syntax needs to be:
1. **Unambiguous** - No positional magic, no context inference
2. **Parseable** - Standard flag syntax, clear delimiters
3. **Composable** - Works in pipes, scripts, CI/CD
4. **Familiar** - Follows kubectl/docker/git conventions where appropriate
5. **Tooling-friendly** - Easy to generate autocomplete, docs, clients

**Alex Thompson**: Let's look at industry standards:

```bash
# Kubectl pattern: resource-oriented
kubectl get pods
kubectl create deployment nginx --image=nginx
kubectl delete service mongodb
kubectl logs deployment/nginx --follow

# Docker pattern: mixed (some resource, some action)
docker ps
docker run nginx
docker stop container-id
docker logs container-id --follow

# Git pattern: action-oriented
git commit -m "message"
git push origin main
git log --oneline
```

Which pattern fits Zen Garden?

**Priya Sharma**: We're managing services (resources) across stones (locations). Resource-oriented makes sense:

```bash
garden-rake services list
garden-rake services create --name mongodb
garden-rake services delete --name mongodb
garden-rake stones list
garden-rake stones show --name stone-02
```

**Marcus Rodriguez**: But what about operations that aren't resource CRUD? Starting/stopping isn't creation/deletion—it's state transition.

**Alex Thompson**: Kubectl solves this with subcommands on verbs:
```bash
kubectl scale deployment nginx --replicas=3
kubectl rollout restart deployment nginx
```

Or we could use resource subcommands:
```bash
garden-rake services start --name mongodb
garden-rake services stop --name mongodb
garden-rake services restart --name mongodb
```

**Yuki Tanaka**: From a cognitive perspective, `services start` is more learnable than `start service`. The resource comes first, establishing context for the action.

**James O'Connor**: Agreed. `garden-rake services start --name mongodb` is self-documenting. The structure is: `garden-rake <resource> <verb> <flags>`.

---

### Proposed Normative Structure

**Priya Sharma**: Here's a complete resource taxonomy:

```
garden-rake <resource> <verb> [flags]

Resources:
  services          - Service instances
  offerings         - Available offerings (catalog)
  stones            - Stones in the garden
  pond              - Pond security
  adoption          - Adoption management
  templates         - Offering templates
  console           - Console output control
  context           - Tending context
  jobs              - Background jobs

Common verbs:
  list              - List resources
  show              - Show resource details
  create            - Create resource
  delete            - Delete resource
  update            - Update resource
  start             - Start (services)
  stop              - Stop (services)
  restart           - Restart (services)
  logs              - Stream logs
```

**Marcus Rodriguez**: This is clean. Every command is structured: `<noun> <verb> <options>`. No positional magic. Flags are explicit. Example:

```bash
# Zen
garden-rake offer mongodb at stone-02

# Normative
garden-rake services create --name mongodb --at stone-02
```

**Alex Thompson**: What about commands that don't fit resource pattern? Like `reconcile` (sync inventory) or `refresh` (update binaries)?

**Priya Sharma**: Those are actions on implicit resources:
```bash
garden-rake services reconcile           # Action on services collection
garden-rake stones upgrade --component moss --from ./binary
```

**James O'Connor**: We need clear docs on when to use which. Let me propose:

### Normative Syntax Rules

1. **Structure**: `garden-rake <resource> <verb> [flags]`
2. **Resources are nouns, plural**: `services`, `stones`, `offerings`
3. **Verbs are standard**: `list`, `show`, `create`, `delete`, `start`, `stop`, `update`
4. **Flags are explicit**: `--name`, `--at`, `--from`, `--to`
5. **No positional arguments** except resource/verb
6. **Collection actions**: Use verb without `--name` flag
   - `garden-rake services list` (list all)
   - `garden-rake services reconcile` (reconcile all)

**Marcus Rodriguez**: What about flags vs subcommands? Compare:
```bash
garden-rake services logs --name mongodb --follow
# vs
garden-rake services mongodb logs --follow
```

**Alex Thompson**: The second is shorter but breaks the `<resource> <verb>` pattern. Flags are more consistent:
```bash
garden-rake services logs --name mongodb --follow
garden-rake services start --name mongodb
garden-rake services stop --name mongodb
```

**Yuki Tanaka**: Consistency reduces cognitive load. Same structure for all commands makes it predictable.

**CONSENSUS**: Normative structure is `garden-rake <resource> <verb> [flags]`. Resources are plural nouns. Verbs are standard CRUD + domain-specific. Flags are explicit and named.

---

## Part 4: 1:1 Mirroring Strategy

### The Translation Challenge

**Priya Sharma**: We need perfect 1:1 semantic mapping. Every zen command must have a normative equivalent and vice versa. Let me start mapping:

| Zen | Normative | Semantics |
|-----|-----------|-----------|
| `offer` | `offerings list` | List available offerings |
| `offer mongodb` | `services create --name mongodb` | Install offering |
| `offer mongodb info` | `offerings show --name mongodb` | Show offering details |
| `list` | `services list` | List services |
| `rest mongodb` | `services stop --name mongodb` | Stop service |
| `wake mongodb` | `services start --name mongodb` | Start service |
| `remove mongodb` | `services delete --name mongodb` | Delete service |
| `observe` | `stones list` | List all stones |
| `observe stone-02` | `stones show --name stone-02` | Show specific stone |
| `watch` | `events stream` | Stream events |
| `watch offering mongodb logs` | `services logs --name mongodb` | Stream service logs |
| `tend` | `context show` | Show context |
| `tend stone-02` | `context set --target stone-02` | Set context |
| `tend --clear` | `context clear` | Clear context |
| `place keystone` | `pond init` | Initialize pond |
| `place stone --code ABC` | `pond join --code ABC` | Join pond |
| `invite` | `pond invite` | Generate invitation |
| `lift stone stone-02` | `pond untrust --name stone-02` | Remove stone from pond |
| `reconcile` | `services reconcile` | Reconcile inventory |
| `upgrade mongodb` | `services update --name mongodb` | Update service |
| `make stone sing` | `console set-mode --mode verbose` | Set verbose console |
| `make stone quiet` | `console set-mode --mode informative` | Set default console |
| `make stone silent` | `console set-mode --mode silent` | Set silent console |
| `refresh moss --from ./binary` | `stones upgrade --component moss --from ./binary` | Upgrade moss binary |
| `take-root` | `stones install-service` | Install as system service |
| `template list` | `templates list` | List templates |
| `template show mongodb` | `templates show --name mongodb` | Show template content |
| `status` | `stones status` | Show local stone status |

**Marcus Rodriguez**: This looks good but I see issues:
1. `offer mongodb` → `services create --name mongodb` is clean
2. But `offer` → `offerings list` is confusing. Why is listing called "offer"?

**Sarah Chen**: Because in natural language, "what can you offer?" means "what do you have available?" The verb "offer" in zen mode means "present the offerings" when used intransitively.

**Yuki Tanaka**: This is a case where zen optimizes for natural language pragmatics (offer = present/give depending on context) while normative optimizes for explicit semantics (offerings list vs services create).

**Alex Thompson**: I think the mapping works, but we need help text that explains this. When someone runs `garden-rake offer --help`, it should say:
```
garden-rake offer [offering] [info]

List or install offerings in your garden.

Zen Patterns:
  garden-rake offer                    List available offerings
  garden-rake offer mongodb            Install mongodb offering
  garden-rake offer mongodb info       Show mongodb details

Normative Equivalents:
  garden-rake offerings list
  garden-rake services create --name mongodb
  garden-rake offerings show --name mongodb
```

**James O'Connor**: Yes! Cross-referencing in help text is crucial for discoverability.

---

### Handling Ambiguities

**Priya Sharma**: Some zen commands don't map cleanly. Consider:
```bash
# Zen: positional "at" syntax
garden-rake offer mongodb at stone-02

# Normative: flag syntax
garden-rake services create --name mongodb --at stone-02
```

What if someone names their stone "at"? In zen syntax, `garden-rake offer mongodb at at` is ambiguous.

**Marcus Rodriguez**: That's why normative uses flags. `--at` is unambiguous. Zen syntax needs parsing rules:

**Alex Thompson**: Parsing precedence:
1. Known keywords (`info`, `at`) are checked first
2. If ambiguous, require explicit disambiguation
3. Or use quotes: `garden-rake offer mongodb at "at"`

**Yuki Tanaka**: We could also detect common conflicts and warn users when naming stones. If someone tries to name a stone "at", warn: "Stone name 'at' may conflict with zen syntax. Consider using 'at-stone' instead."

**CONSENSUS**: Zen syntax accepts some ambiguity, resolved by parsing rules and smart warnings. Normative syntax uses flags to avoid ambiguity entirely.

---

### Proposed Mapping Table (Complete)

**James O'Connor**: I'll compile the complete mapping. This becomes our source of truth:

```markdown
## Service Lifecycle

| Zen | Normative | Description |
|-----|-----------|-------------|
| `list [--at <target>]` | `services list [--at <target>]` | List services on stone |
| `offer <name> [at <target>]` | `services create --name <name> [--at <target>]` | Create/install service |
| `rest <name> [--at <target>]` | `services stop --name <name> [--at <target>]` | Stop service |
| `wake <name> [--at <target>]` | `services start --name <name> [--at <target>]` | Start service |
| `remove <name> [--at <target>]` | `services delete --name <name> [--at <target>]` | Delete service |
| `upgrade <name> [--at <target>]` | `services update --name <name> [--at <target>]` | Update service |
| *(not implemented)* | `services restart --name <name> [--at <target>]` | Restart service |
| *(proposed: nourish)* | `services reconfigure --name <name> --config <file> [--at <target>]` | Update service config |

## Offering Discovery

| Zen | Normative | Description |
|-----|-----------|-------------|
| `offer [--at <target>]` | `offerings list [--at <target>]` | List available offerings |
| `offer <name> info [--at <target>]` | `offerings show --name <name> [--at <target>]` | Show offering details |
| *(not implemented)* | `offerings refresh [--at <target>]` | Refresh catalog |

## Adoption & External Services

| Zen | Normative | Description |
|-----|-----------|-------------|
| *(proposed: discover adoptable)* | `adoption list-adoptable [--at <target>]` | List adoptable containers |
| *(proposed: adopt <name>)* | `adoption adopt --name <name> [--at <target>]` | Adopt container |
| *(proposed: list adopted)* | `adoption list-adopted [--at <target>]` | List adopted services |
| *(proposed: list borrowed)* | `adoption list-borrowed [--at <target>]` | List borrowed services |
| *(proposed: release <name>)* | `adoption unadopt --name <name> [--at <target>]` | Unadopt service |

## Garden Observation

| Zen | Normative | Description |
|-----|-----------|-------------|
| `observe [stone]` | `stones list [--name <stone>]` | List stones or show specific stone |
| `status [--at <target>]` | `stones status [--at <target>]` | Show local stone status |
| *(not implemented)* | `stones show --name <name>` | Show specific stone details |
| *(not implemented)* | `metrics get [--at <target>]` | Get stone metrics |
| `watch [--at <target>]` | `events stream [--at <target>]` | Stream events |
| `watch offering <name> logs [--at <target>]` | `services logs --name <name> [--at <target>]` | Stream service logs |

## Templates

| Zen | Normative | Description |
|-----|-----------|-------------|
| `template list [--at <target>]` | `templates list [--at <target>]` | List offering templates |
| `template show <name> [--at <target>]` | `templates show --name <name> [--at <target>]` | Show template content |

## Inventory

| Zen | Normative | Description |
|-----|-----------|-------------|
| `reconcile [--drop-invalid] [--at <target>]` | `services reconcile [--drop-invalid] [--at <target>]` | Reconcile inventory |
| *(proposed: heal)* | `offerings heal [--drop-invalid] [--at <target>]` | Heal garden (zen alias) |

## Pond Security

| Zen | Normative | Description |
|-----|-----------|-------------|
| `place keystone [--passphrase <pass>] [--at <target>]` | `pond init [--passphrase <pass>] [--at <target>]` | Initialize pond |
| *(not implemented)* | `pond status [--at <target>]` | Show pond status |
| `invite [--at <target>]` | `pond invite [--at <target>]` | Generate invitation code |
| `place stone --code <code> [--at <target>]` | `pond join --code <code> [--at <target>]` | Join pond |
| *(not implemented)* | `pond remove [--at <target>]` | Remove pond |
| `lift stone <name> [--at <target>]` | `pond untrust --name <name> [--at <target>]` | Remove stone from pond |

## Stone Operations

| Zen | Normative | Description |
|-----|-----------|-------------|
| `refresh <component> --from <path> [--at <target>]` | `stones upgrade --component <component> --from <path> [--at <target>]` | Upgrade binary |
| *(not implemented)* | `stones shutdown [--at <target>]` | Shutdown stone |
| `take-root [at <target>]` | `stones install-service [--at <target>]` | Install as system service |

## Console Control

| Zen | Normative | Description |
|-----|-----------|-------------|
| `make stone sing [--forever] [--at <target>]` | `console set-mode --mode verbose [--persist] [--at <target>]` | Set verbose output |
| `make stone quiet [--at <target>]` | `console set-mode --mode informative [--at <target>]` | Set informative output |
| `make stone silent [--at <target>]` | `console set-mode --mode silent [--at <target>]` | Set silent output |
| `make stone minimal [--at <target>]` | `console set-mode --mode minimal [--at <target>]` | Set minimal output |
| *(not implemented)* | `console get-mode [--at <target>]` | Get current mode |

## Context Management

| Zen | Normative | Description |
|-----|-----------|-------------|
| `tend [--verbose]` | `context show [--verbose]` | Show tending context |
| `tend <target>` | `context set --target <target>` | Set tending context |
| `tend --clear` | `context clear` | Clear tending context |

## Discovery (Proposed)

| Zen | Normative | Description |
|-----|-----------|-------------|
| `explore` | `stones discover` | Discover stones on network |
| `check <stone>` | `stones health-check --name <stone>` | Check stone health |
```

**Marcus Rodriguez**: This is comprehensive. I can script with the normative side, and I can read the zen side in docs without confusion.

**Sarah Chen**: And the zen commands carry meaning. When I see `rest mongodb`, I know someone is intentionally pausing a service, not just mechanically stopping it.

**Alex Thompson**: The key is tooling. We need:
1. `--help` text that shows both syntaxes
2. Shell completion for both
3. Error messages that suggest normative if zen fails
4. Docs that teach both with clear use-cases

**CONSENSUS**: 1:1 mapping is achievable. Complete mapping table defines the contract.

---

## Part 5: Implementation Strategy

### Phase 1: Foundation (Immediate)

**Alex Thompson**: What's the implementation path?

**Marcus Rodriguez**: Start with normative syntax. It's simpler—no positional parsing, no context inference. Get the resource/verb structure working:

```bash
garden-rake services list
garden-rake services create --name mongodb
garden-rake services stop --name mongodb
```

Once normative works, zen becomes an alias layer on top.

**Priya Sharma**: Exactly. Normative is the canonical representation. Zen is syntactic sugar that translates to normative internally:

```rust
// Pseudo-code
match zen_command {
    Offer { name: None, .. } =>
        normative::offerings::list(),
    Offer { name: Some(n), info: false, .. } =>
        normative::services::create(n),
    Offer { name: Some(n), info: true, .. } =>
        normative::offerings::show(n),
}
```

**Alex Thompson**: This keeps the implementation clean. One execution path, multiple syntax front-ends.

**James O'Connor**: Document the translation explicitly. Include a `garden-rake translate` command:
```bash
$ garden-rake translate "offer mongodb at stone-02"
Normative: garden-rake services create --name mongodb --at stone-02

$ garden-rake translate "services create --name mongodb"
Zen: garden-rake offer mongodb
```

**Yuki Tanaka**: Brilliant. This makes the duality explicit and teachable.

---

### Phase 2: Tooling & Documentation

**Alex Thompson**: We need:
1. **Shell completion** - Both zsh and bash, covering both syntaxes
2. **Man pages** - Traditional Unix docs
3. **Interactive help** - `garden-rake help offer` shows zen + normative
4. **Error messages** - Suggest correct syntax in both modes

**James O'Connor**: And a comprehensive guide:
- "When to use Zen vs Normative"
- "Zen Syntax Tutorial" - narrative, examples, philosophy
- "Normative Syntax Reference" - structured, complete, script-focused
- "Translation Guide" - mapping table with examples

**Marcus Rodriguez**: VSCode extension or language server for syntax highlighting and inline docs?

**Alex Thompson**: Maybe phase 3. Let's nail the CLI first.

---

### Phase 3: Validation & Refinement

**Yuki Tanaka**: We need user testing. Give both syntaxes to:
1. New users (learnability)
2. DevOps engineers (scripting usability)
3. Interactive users (ergonomics)

Measure:
- Time to first success
- Error rates
- Subjective satisfaction
- Which syntax they prefer for which tasks

**Sarah Chen**: And qualitative feedback. Do zen users feel the metaphor enhances or obscures? Do normative users find it verbose or clear?

**Marcus Rodriguez**: I'd want to see real scripts. Can I write a 200-line bash script using normative syntax that's maintainable? Can I pair-program over SSH using zen syntax and have my teammate understand?

**Priya Sharma**: Also test edge cases. Services named "at", "info", "list". Stone names that collide with verbs. Make sure parsing is robust.

---

## Part 6: Open Questions & Risks

### Question 1: Cognitive Switching Cost

**Yuki Tanaka**: If users learn zen, how hard is it to read normative? And vice versa? Is there interference?

**James O'Connor**: That's why cross-referencing in help text is critical. Every zen command's help should show the normative equivalent. Users can learn both in parallel.

**Sarah Chen**: Or they might specialize. Interactive users might never learn normative. Scripters might never learn zen. That's okay if both are complete.

**RISK**: Medium. Mitigated by good docs and translation tooling.

---

### Question 2: Maintenance Burden

**Marcus Rodriguez**: We're committing to maintain two syntaxes forever. Every new feature needs both. Every bug fix needs both. That's double the testing surface.

**Priya Sharma**: If normative is canonical and zen is a translation layer, the burden is less than it seems. We're not maintaining two implementations—we're maintaining one implementation and one parser.

**Alex Thompson**: Still, every release note needs to document both syntaxes. Every example needs both versions. Docs are doubled.

**RISK**: Medium-High. Mitigated by treating zen as a facade over normative, not a separate implementation.

---

### Question 3: Third-Party Tooling

**Alex Thompson**: What about tools that wrap garden-rake? Ansible modules, Terraform providers, Kubernetes operators? Do they use normative exclusively?

**Marcus Rodriguez**: They should. Normative is the stable API. Zen is for humans at terminals.

**James O'Connor**: We need to document this clearly: "For programmatic use, always use normative syntax."

**RISK**: Low. Clear guidance in docs prevents confusion.

---

### Question 4: Language/Cultural Barriers

**Sarah Chen**: Zen metaphors are culturally specific. "Offer", "rest", "wake" make sense in English with garden metaphors. Do they translate?

**Yuki Tanaka**: This is a real concern for internationalization. Normative syntax is language-agnostic—`services create` translates easily. Zen might not.

**Sarah Chen**: We might need to accept that zen is English-only, or have locale-specific zen vocabularies. But that's a future problem.

**RISK**: Medium (long-term). Mitigated by having normative as language-agnostic fallback.

---

### Question 5: Evolution & Stability

**Priya Sharma**: What's our commitment to backward compatibility? If we realize `offer` is confusing, can we change it to `install`?

**Marcus Rodriguez**: No. Once we ship dual syntax, both are locked. Breaking changes are unacceptable for production CLIs.

**Alex Thompson**: We need to get this right in v1. That's why this discussion is so important.

**RISK**: High. Mitigated by thorough design review (this document) before implementation.

---

## Part 7: Radical Redesign Proposals

**FACILITATOR**: We've established a solid foundation. But before we finalize, let's challenge our assumptions. What if we're wrong? What alternative designs should we consider?

---

### Proposal A: Reconsider "rest" and "wake"

**Marcus Rodriguez**: I have to be honest—I've been thinking about "rest" and "wake" since we started. They're poetic, but are they clear? When I'm in an incident at 3 AM, do I want to type `rest` or `stop`?

In my head, "rest" implies temporary pause with intent to resume. "Stop" is definitive. But what if I want to permanently stop something? Do I still use `rest`? That's semantic confusion.

**Sarah Chen**: The metaphor is deliberate. In a garden, nothing is permanent. Services rest (dormancy) and wake (bloom). The lifecycle is circular, not terminal. Even `remove` in zen isn't destruction—it's removal from the garden, but the offering still exists.

**Priya Sharma**: But Marcus has a point. "Rest" overloads the semantics. Consider:
- `rest mongodb` - temporary maintenance stop
- `rest mongodb --permanent` - ????

The modifier breaks the metaphor. You can't "permanently rest."

**Yuki Tanaka**: What if we distinguish:
- `rest` - temporary, intent to resume (maintenance mode)
- `dormant` - long-term pause (winter)
- `remove` - complete removal from garden

**Alex Thompson**: Now we have three verbs for stop operations. Kubectl just has `delete` for remove and stops implicitly when you scale to zero. Docker has `stop` and `rm`. We're adding cognitive overhead.

**Sarah Chen**: Counter-proposal: Keep `rest` and `wake`, but embrace the metaphor fully:
- `rest` - ALL stopping, temporary or not
- `wake` - ALL starting
- `remove` - remove from garden (but container still exists, like pulling a plant)
- `uproot` - complete destruction (delete container)

Now the metaphor is consistent AND we have granularity.

**Marcus Rodriguez**: I could live with that. But normative still uses `stop`, `start`, `delete`, `destroy`?

**Priya Sharma**: Yes. Normative doesn't need metaphors:
```
services stop    = rest
services start   = wake
services delete  = remove (stops + removes from registry)
services destroy = uproot (stops + removes container)
```

**James O'Connor**: This gives us nuance. Power users can distinguish "remove from registry but keep container" vs "destroy everything." Zen users get poetic verbs. Win-win.

**PROPOSAL**:
- Zen: `rest` / `wake` / `remove` / `uproot`
- Normative: `stop` / `start` / `delete` / `destroy`
- Semantics: `delete` = soft delete (registry only), `destroy` = hard delete (container)

**Vote**: 4 in favor (Yuki, Priya, James, Sarah), 2 abstain (Marcus: "I'll trust you"), Alex: "Let's prototype and test"

---

### Proposal B: Rethink "offer" Overloading

**Priya Sharma**: I want to revisit `offer` being three commands in one:
- `offer` - list offerings
- `offer mongodb` - install offering
- `offer mongodb info` - show offering details

This violates single-responsibility. What if we split it?

**Alternative Zen Verbs**:
```
browse              # List offerings catalog
offer <name>        # Install offering
inspect <name>      # Show offering details
```

Or:
```
offerings           # List (noun as command)
offer <name>        # Install
describe <name>     # Show details
```

**Sarah Chen**: "Browse" and "inspect" are good, but they break the metaphor. You don't "browse" a garden, you "wander" or "explore". You don't "inspect" a plant, you "examine" or "observe".

**Yuki Tanaka**: But "observe" is already taken for garden-wide viewing. We need distinction.

**Alternative**:
```
wander              # Browse offerings (explore the catalog)
offer <name>        # Install offering
examine <name>      # Show offering details before installing
```

**Marcus Rodriguez**: "Wander" to list services? That's too poetic. I'd never guess that.

**Alex Thompson**: What if we keep `offer` overloading but make the help text exceptional?
```
$ garden-rake offer --help

garden-rake offer [offering] [action]

The zen of offering: presenting what can be given, and giving it.

Usage patterns:
  garden-rake offer                    List what offerings are available
  garden-rake offer <name>             Install an offering into your garden
  garden-rake offer <name> info        Learn about an offering before installing

Think of it this way: "offer" with no argument means "show me what you offer."
"offer mongodb" means "I offer/give mongodb to my garden."
```

**James O'Connor**: I like that. Overloading is okay if the help system teaches the pattern explicitly. And it matches natural language: "What do you offer?" vs "I offer you this."

**Sarah Chen**: Agreed. The verb "offer" works both ways in English. We're leveraging that.

**PROPOSAL**: Keep `offer` overloading, but invest in exceptional help text and examples.

**Vote**: 5 in favor, 1 against (Priya: "It's still semantically impure"), Marcus abstains

---

### Proposal C: "At" Keyword - Too Overloaded?

**Marcus Rodriguez**: The word "at" appears in three contexts:
1. Target stone: `offer mongodb at stone-02`
2. Service named "at": `rest at`
3. User might say "offer at" meaning "offer the 'at' service"

This is a parsing nightmare.

**Alternative Proposals**:

**Option 1: Change keyword**
```
offer mongodb on stone-02       # "on" instead of "at"
offer mongodb to stone-02       # "to" instead of "at"
offer mongodb -> stone-02       # arrow operator
```

**Option 2: Flag syntax in zen too**
```
offer mongodb --stone stone-02
offer mongodb --target stone-02
```

**Option 3: Require quoting for service names that match keywords**
```
rest "at"                       # Force quotes for conflicting names
offer "info"                    # Force quotes for conflicting names
```

**Sarah Chen**: "On" is interesting. "Offer mongodb on stone-02" - like placing something on a surface. Spatially coherent.

**Yuki Tanaka**: "To" implies directionality. "Send mongodb to stone-02." Also works.

**Alex Thompson**: I prefer "on". It's a single word, clear, and spatially meaningful. Plus:
```
offer mongodb on stone-02
place keystone on stone-01
lift stone on stone-02... wait, that doesn't work
```

**Priya Sharma**: What about using `@` symbol?
```
offer mongodb @stone-02
rest mongodb @stone-02
```
Shorter, unambiguous, won't conflict with service names.

**Marcus Rodriguez**: I like `@` for scripting, but for zen? Symbols aren't words. It breaks the natural language flow.

**James O'Connor**: Could we support both?
```
offer mongodb on stone-02       # Natural zen
offer mongodb @stone-02         # Shorthand zen
offer mongodb --at stone-02     # Flag style (allowed in zen too)
```

**Yuki Tanaka**: Multiple ways to do the same thing increases cognitive load. Pick one.

**PROPOSAL**: Replace "at" with "on" in zen syntax. More spatially coherent, less conflict with service names.

**Vote**: 4 in favor (Sarah, Yuki, Priya, James), 2 prefer "@" symbol (Marcus, Alex)

**REVISED PROPOSAL**: Use "on" as primary, but silently accept "@" as alias for users who type it naturally.

**Vote**: 6 unanimous

---

### Proposal D: Normative - Resource First or Verb First?

**Alex Thompson**: We decided on `garden-rake services start`. But kubectl uses `kubectl get pods`. Git uses `git commit`. Docker uses `docker run`. There's no industry consensus on resource-first vs verb-first.

**Comparison**:
```
# Resource-first (our current proposal)
garden-rake services list
garden-rake services create --name mongodb
garden-rake services start --name mongodb

# Verb-first (alternative)
garden-rake list services
garden-rake create service --name mongodb
garden-rake start service --name mongodb

# Hybrid (context-dependent)
garden-rake list services           # Discovery verbs first
garden-rake service start mongodb   # Action verbs after resource
```

**Priya Sharma**: Resource-first is better for autocompletion. You type `garden-rake services <TAB>` and get all service operations. Verb-first means you type `garden-rake <TAB>` and get a huge list of verbs across all resources.

**Marcus Rodriguez**: Good point. Resource-first also groups mentally: "I'm working with services right now, what can I do?" vs "I want to start something, what can I start?"

**Yuki Tanaka**: Linguistically, resource-first is object-oriented. "Services: list them, create one, start one." Verb-first is action-oriented. "List: services, stones, offerings."

**Alex Thompson**: Let's test discoverability. New user flow:

Resource-first:
```
$ garden-rake <TAB>
services offerings stones pond adoption templates console context jobs

$ garden-rake services <TAB>
list show create delete start stop restart update logs reconcile
```

Verb-first:
```
$ garden-rake <TAB>
list show create delete start stop restart update logs get set ... [50+ verbs]

$ garden-rake list <TAB>
services offerings stones templates
```

**James O'Connor**: Resource-first is more discoverable. Category first, then actions within that category.

**PROPOSAL**: Keep resource-first structure for normative syntax.

**Vote**: 6 unanimous

---

### Proposal E: Should Zen Have Subcommands?

**Sarah Chen**: Current zen is flat: `offer mongodb`. But we have `template show mongodb`. Why the inconsistency?

**Options**:

**Option 1: Fully flat (remove subcommands)**
```
offerings              # Instead of "offer"
offer mongodb          # Install
describe mongodb       # Instead of "offer mongodb info"
templates              # Instead of "template list"
show-template mongodb  # Instead of "template show mongodb"
```

**Option 2: Embrace subcommands**
```
offer list             # List offerings
offer install mongodb  # Install offering
offer info mongodb     # Show offering details
template list          # List templates
template show mongodb  # Show template
```

**Option 3: Hybrid (current state)**
Keep overloading for common verbs, use subcommands for namespaced operations.

**Yuki Tanaka**: Option 1 feels like we're avoiding the real pattern. Option 2 is more structured but verbose. Option 3 is pragmatic but inconsistent.

**Priya Sharma**: What if we use the principle: **Core garden actions are flat verbs. Administrative/inspection actions use subcommands.**

Core garden actions (zen verbs):
- `offer`, `rest`, `wake`, `remove`, `observe`, `watch`, `tend`, `place`, `invite`, `lift`

Administrative/inspection (subcommands):
- `template list/show`
- `console mode/get`
- `context show/set/clear`

**Marcus Rodriguez**: That's a good principle. Daily operations are zen verbs. Maintenance operations are structured subcommands.

**PROPOSAL**: Zen uses flat verbs for garden operations, subcommands for administrative operations.

**Vote**: 5 in favor, 1 abstain (Alex: "I'd prefer full consistency but this is pragmatic")

---

### Proposal F: "Adopt" and "Borrow" - Missing Zen Commands

**Sarah Chen**: We have API endpoints for adoption (existing containers) and borrowed services (external network services), but no zen commands. What should they be?

**Adoption** (managing existing containers):
```
# API: GET /api/v1/adoption/adoptable
discover adoptable    # List containers that can be adopted
find strays          # Zen alternative - "stray containers"

# API: POST /api/v1/adoption/adopt
adopt <name>         # Adopt a container into the garden

# API: GET /api/v1/adoption/adopted
list adopted         # Show adopted services
adopted              # Noun as command

# API: DELETE /api/v1/adoption/adopted/:name
release <name>       # Release an adopted service back to the wild
```

**Borrowed** (external network services):
```
# API: GET /api/v1/adoption/borrowed
list borrowed        # Show borrowed services
borrowed             # Noun as command

# API: POST /api/v1/adoption/borrow (hypothetical)
borrow <service> from <url>    # Borrow external service

# API: DELETE /api/v1/adoption/borrowed/:name
return <name>        # Return a borrowed service
```

**Marcus Rodriguez**: "Find strays" is evocative. I like it. Containers running without a home.

**Yuki Tanaka**: "Adopt" and "release" are perfect. Clear semantics, match the metaphor.

**Priya Sharma**: What about "borrow" and "return"? Should zen even expose this? It's advanced.

**Sarah Chen**: Borrowing is beautiful. You're acknowledging that some services live outside your garden, but you want to integrate them. "Borrow redis from company-cache-01:6379."

**James O'Connor**: The metaphor works. You borrow from a neighbor's garden.

**PROPOSAL**:
Zen commands:
- `find strays` → list adoptable
- `adopt <name>` → adopt container
- `adopted` → list adopted
- `release <name>` → unadopt
- `borrowed` → list borrowed
- `borrow <service> from <url>` → register external service
- `return <name>` → unregister external service

Normative commands:
- `adoption list-adoptable`
- `adoption adopt --name <name>`
- `adoption list-adopted`
- `adoption unadopt --name <name>`
- `adoption list-borrowed`
- `adoption borrow --name <service> --url <url>`
- `adoption unborrow --name <name>`

**Vote**: 6 unanimous

---

### Proposal G: "Make" Verb - Too Vague?

**Alex Thompson**: `make stone sing` is poetic but opaque. What does "make" convey? It's a factory verb—"make X do Y"—but it's generic.

**Alternatives**:
```
# Keep current
make stone sing                    # Set verbose mode
make stone quiet                   # Set informative mode
make stone silent                  # Set silent mode

# Alternative: Use "set" (but that's normative-feeling)
set stone verbose
set stone informative
set stone silent

# Alternative: Use specific verbs
tune stone verbose                 # "Tune" the output
voice stone loud/soft/silent       # "Voice" controls sound
adjust stone verbose               # "Adjust" settings
```

**Sarah Chen**: "Make" is intentional. It's about transformation. "Make the stone sing" = transform its behavior to singing (verbose output). It's active, creative.

**Yuki Tanaka**: But "sing" is doing the heavy lifting, not "make". Could we drop "make"?

```
sing                              # Make stone verbose (the stone sings)
quiet                             # Make stone quiet
silence                           # Make stone silent
```

**Marcus Rodriguez**: That's cleaner. The verb itself is the transformation.

**Priya Sharma**: But then we need a target. "Sing what? The service? The stone?"

**James O'Connor**: What if we use adverbs?
```
speak loudly                      # Verbose
speak softly                      # Informative
speak silently                    # Silent (contradiction, but zen!)
hush                              # Minimal
```

**Sarah Chen**: I love "speak loudly" and "speak softly." But "speak silently" is philosophically beautiful—the stone speaks, but we choose not to hear.

**Alex Thompson**: This is getting too poetic. Let's just use:
```
verbose [--persist]               # Enable verbose mode
quiet [--persist]                 # Default mode
silent [--persist]                # Silent mode
```

**Yuki Tanaka**: But those don't feel like garden actions. They're mode switches.

**PROPOSAL**: Keep `make stone <mode>` for now, but add it to user testing. If users find it confusing, pivot to simpler verbs.

**Vote**: 4 in favor (keep current), 2 prefer simpler verbs (Alex, Marcus)

---

### Proposal H: Stone Names and Scope - Implicit vs Explicit

**Marcus Rodriguez**: Right now, most commands are stone-scoped via tending. But `observe` is garden-wide. This is inconsistent.

**Current behavior**:
```
tend stone-02                     # Set context
offer mongodb                     # Installs on stone-02 (implicit scope)
list                              # Lists services on stone-02 (implicit scope)
observe                           # Shows ALL stones (explicit garden scope)
observe stone-02                  # Shows stone-02 only
```

**Should we make scope more explicit?**

**Option 1: Keep implicit (current)**
- Tending sets default scope
- Garden-wide commands explicitly say so in their semantics

**Option 2: Require explicit scope markers**
```
offer mongodb on this             # Explicit "this stone"
offer mongodb on stone-02         # Explicit target stone
offer mongodb on all              # Explicit "all stones" (future)
```

**Option 3: Separate commands for local vs garden-wide**
```
list                              # Local (tended stone)
list-all                          # Garden-wide
observe                           # Garden-wide
status                            # Local (tended stone)
```

**Priya Sharma**: Option 3 is clearest. `list` is scoped, `observe` is garden-wide. Different verbs, different scopes.

**Yuki Tanaka**: Agreed. Users will learn: "list" for current stone, "observe" for the whole garden.

**Sarah Chen**: The metaphor supports this. You "list" what's immediately around you. You "observe" the panorama.

**PROPOSAL**: Keep separate verbs for stone-scoped vs garden-wide operations. Document the distinction clearly.

**Vote**: 6 unanimous

---

### Proposal I: Positional "from" for Borrow?

**Sarah Chen**: I suggested `borrow redis from company-cache:6379`. But is "from" overloaded like "at" was?

**Priya Sharma**: Less risky. "from" is less likely to be a service name. But we should still support flags:
```
# Zen positional
borrow redis from redis://company-cache:6379

# Zen flags (also supported)
borrow redis --from redis://company-cache:6379

# Normative
adoption borrow --name redis --url redis://company-cache:6379
```

**PROPOSAL**: Support both positional "from" and flag "--from" in zen syntax.

**Vote**: 6 unanimous

---

### Proposal J: Ceremony Commands - Should They Be Separate?

**James O'Connor**: The proposals mention "ceremonies"—guided multi-step workflows. Should these be zen commands or a separate mode?

**Options**:

**Option 1: Separate ceremony mode**
```
garden-rake ceremony first-stone
garden-rake ceremony place-keystone
garden-rake ceremony join-garden
```

**Option 2: Zen commands with --guided flag**
```
place keystone --guided           # Interactive ceremony
invite --guided                   # Interactive ceremony
```

**Option 3: Separate binary**
```
garden-ceremony first-stone       # Separate tool for ceremonies
```

**Yuki Tanaka**: Ceremonies are about learning and safety. They should be gentle, guided experiences. Separate commands make them discoverable.

**Marcus Rodriguez**: I'd never use ceremonies in scripts. They're for humans, especially beginners. Option 1 makes sense.

**PROPOSAL**: Add `ceremony` as a separate resource in normative, zen TBD:
```
# Normative
garden-rake ceremonies list
garden-rake ceremonies start --name first-stone

# Zen (TBD - maybe just list ceremonies and let user run them?)
ceremonies                        # List available ceremonies
ceremony first-stone              # Run ceremony
```

**Vote**: 5 in favor, 1 abstain (Priya: "I need to see the design first")

---

**SUMMARY OF RADICAL PROPOSALS**:

1. ✅ **Adopted**: Add `uproot` as distinct from `remove` (hard vs soft delete)
2. ✅ **Adopted**: Keep `offer` overloading with excellent help text
3. ✅ **Adopted**: Change "at" to "on" for stone targeting
4. ✅ **Adopted**: Keep resource-first normative structure
5. ✅ **Adopted**: Zen uses flat verbs for garden ops, subcommands for admin
6. ✅ **Adopted**: Add adoption commands: `find strays`, `adopt`, `release`, `borrow`, `return`
7. 🔶 **Testing**: Keep `make stone <mode>`, test with users
8. ✅ **Adopted**: Separate verbs for stone-scoped vs garden-wide (list vs observe)
9. ✅ **Adopted**: Support "from" positional and flag in borrow
10. ✅ **Adopted**: Add `ceremony` as separate command group

---

## Part 8: Final Recommendations (Revised)

### For Zen Syntax (REVISED with Radical Proposals)

1. **Core verbs retained with refinements**:
   - Lifecycle: `offer`, `rest`, `wake`, `remove`, `uproot` (NEW)
   - Adoption: `find strays` (NEW), `adopt` (NEW), `release` (NEW), `borrow` (NEW), `return` (NEW)
   - Observation: `observe`, `watch`, `list`, `status`
   - Context: `tend`
   - Pond: `place`, `invite`, `lift`
   - Admin: `make stone <mode>`, `template`, `ceremony` (NEW)

2. **Positional keywords revised**:
   - Use `on` instead of `at` for stone targeting: `offer mongodb on stone-02`
   - Support `@` as silent alias: `offer mongodb @stone-02`
   - Support `from` for borrowed services: `borrow redis from redis://cache:6379`

3. **Overloading embraced with excellent help**:
   - `offer` = list/install/info (context-dependent)
   - Help text must explain all forms explicitly
   - Natural language pragmatics justify the ambiguity

4. **Flat verbs for core operations, subcommands for admin**:
   - Core: `offer mongodb`, `rest mongodb`, `adopt container-xyz`
   - Admin: `template show mongodb`, `ceremony first-stone`

5. **Document metaphors extensively**:
   - Explain why "rest" not "stop", "offer" not "install"
   - Emphasize garden metaphor consistency
   - `uproot` vs `remove`: hard delete vs soft delete

6. **Parsing precedence rules**:
   - Keywords (`on`, `from`, `info`) parsed before service names
   - Warn users if service names conflict with keywords
   - Support quoting for edge cases: `rest "info"`

7. **Reserved keywords** (cannot be used as stone or service names):
   - `keystone` - Special pond concept (the founding stone). Used in `place keystone`, `lift keystone`.
   - `stone` - CLI keyword for stone operations (e.g., `lift stone <name>`)
   - `strays` - CLI keyword for `find strays` command
   - `on`, `from`, `info` - Positional keywords

### For Normative Syntax (REVISED with Radical Proposals)

1. **Structure**: `garden-rake <resource> <verb> [flags]` (resource-first confirmed)
2. **Resources**: Plural nouns (services, stones, offerings, adoption, templates, console, context, pond, ceremonies)
3. **Verbs**: Standard CRUD + domain-specific
   - CRUD: `list`, `show`, `create`, `delete`, `destroy` (NEW - hard delete)
   - Lifecycle: `start`, `stop`, `restart`, `update`
   - Domain: `adopt`, `unadopt`, `borrow`, `unborrow`, `reconcile`, `upgrade`
4. **Flags**: Explicit named flags
   - `--name` - resource name
   - `--on` or `--at` - target stone (both supported, `--on` preferred)
   - `--from` - source (binaries, URLs)
   - `--url` - service URLs
   - `--mode` - console modes
   - `--component` - stone components (moss/rake)
5. **No positional args**: Except resource and verb (strict)
6. **Consistency**: Same pattern for all commands

### For Both

1. **1:1 mapping**: Every command in one syntax has exact equivalent in the other
2. **Cross-reference help**: All help text shows both syntaxes
3. **Translation tool**: `garden-rake translate` command for learning/conversion
4. **Comprehensive docs**: Separate guides for zen (tutorial) and normative (reference)
5. **Error messages**: Suggest correct syntax in both modes
6. **Testing**: Test both syntaxes for all features
7. **Canonical form**: Normative is canonical, zen is facade (simplifies implementation)

---

## Part 8: Next Steps

### Design Phase (Current)
- ✅ Surface area analysis complete
- ✅ Multi-disciplinary discussion complete
- ⏳ **NEXT**: Create formal CLI syntax specification document
- ⏳ Review specification with stakeholders
- ⏳ Finalize syntax before implementation

### Implementation Phase 1: Normative Foundation
- Implement resource/verb parser
- Implement all normative commands matching API surface
- Write tests for normative syntax
- Write man pages and help text

### Implementation Phase 2: Zen Facade
- Implement zen parser (positional + keyword detection)
- Map zen commands to normative execution
- Write tests for zen syntax
- Update help text with cross-references

### Implementation Phase 3: Tooling
- Shell completion (zsh, bash) for both syntaxes
- `garden-rake translate` command
- Error message improvements
- Documentation finalization

### Validation Phase
- User testing (new users, DevOps engineers, interactive users)
- Edge case testing (naming conflicts, ambiguous syntax)
- Performance testing (parsing overhead)
- Documentation review

### Release Phase
- RC release with both syntaxes
- Gather feedback
- Iterate based on real-world usage
- v1.0 release with locked syntax

---

## Appendix: Example Sessions

### Example 1: Interactive User (Zen)

```bash
$ garden-rake offer
Available offerings:
  📦 mongodb (Database)
  📦 redis (Cache)
  🔍 elasticsearch (Search)
  📊 grafana (Monitoring)

$ garden-rake offer mongodb
Installing mongodb...
✓ Created service zen-offering-mongodb
✓ Service is running

$ garden-rake list
Services:
  mongodb (running, 2m ago)

$ garden-rake rest mongodb
✓ Service mongodb resting

$ garden-rake wake mongodb
✓ Service mongodb awakened
```

### Example 2: Script User (Normative)

```bash
#!/bin/bash
set -euo pipefail

# Setup mongodb on stone-01
garden-rake services create \
  --name mongodb \
  --at stone-01

# Wait for healthy
while [[ $(garden-rake services show --name mongodb --at stone-01 --format json | jq -r '.status') != "running" ]]; do
  sleep 5
done

# Configure monitoring
garden-rake services create \
  --name grafana \
  --at stone-01

echo "Setup complete"
```

### Example 3: Troubleshooting (Mixed)

```bash
# User discovers issue with zen
$ garden-rake observe
Stone: stone-01 (thriving)
  mongodb: withering ❌

# Investigates with zen
$ garden-rake watch offering mongodb logs
[ERROR] Connection refused...

# Fixes with normative (in script)
$ garden-rake services restart --name mongodb --at stone-01

# Confirms with zen
$ garden-rake observe
Stone: stone-01 (thriving)
  mongodb: thriving ✓
```

---

## Conclusion

This discussion establishes a foundation for implementing Zen Garden's dual-ergonomics CLI. Key insights:

1. **Dual syntax is justified** by serving distinct cognitive modes (exploration vs automation)
2. **Zen optimizes for human joy and natural language**; normative optimizes for precision and scripting
3. **1:1 mirroring is achievable** with clear mapping table and translation tooling
4. **Normative is canonical**; zen is a facade (simplifies implementation)
5. **Comprehensive tooling is critical** (help text, shell completion, translation, docs)
6. **User testing will validate** assumptions before v1 lock-in

The next step is to formalize this discussion into a **CLI Syntax Specification** document with complete grammar, examples, and implementation guidance.

---

## Appendix D: Revised Complete 1:1 Mapping (Post-Radical Redesign)

### Service Lifecycle (REVISED)

| Zen | Normative | Semantics | Changes |
|-----|-----------|-----------|---------|
| `list [--on <target>]` | `services list [--on <target>]` | List services on stone | Changed `--at` → `--on` |
| `offer <name> [on <target>]` | `services create --name <name> [--on <target>]` | Create/install service | Changed `at` → `on` |
| `rest <name> [on <target>]` | `services stop --name <name> [--on <target>]` | Stop service | Changed `at` → `on` |
| `wake <name> [on <target>]` | `services start --name <name> [--on <target>]` | Start service | Changed `at` → `on` |
| `remove <name> [on <target>]` | `services delete --name <name> [--on <target>]` | Soft delete (registry only) | Changed `at` → `on` |
| `uproot <name> [on <target>]` | `services destroy --name <name> [--on <target>]` | **NEW**: Hard delete (destroy container) | **NEW COMMAND** |
| `upgrade <name> [on <target>]` | `services update --name <name> [--on <target>]` | Update service | Changed `at` → `on` |
| *(not yet implemented)* | `services restart --name <name> [--on <target>]` | Restart service | API exists |

**Note**: All zen commands also accept `@` as alias for `on`: `offer mongodb @stone-02`

### Offering Discovery (REVISED)

| Zen | Normative | Semantics | Changes |
|-----|-----------|-----------|---------|
| `offer [on <target>]` | `offerings list [--on <target>]` | List available offerings | Changed `at` → `on` |
| `offer <name> info [on <target>]` | `offerings show --name <name> [--on <target>]` | Show offering details | Changed `at` → `on` |
| *(not implemented)* | `offerings refresh [--on <target>]` | Refresh catalog | API exists |

### Adoption & Borrowed Services (NEW)

| Zen | Normative | Semantics | Changes |
|-----|-----------|-----------|---------|
| `find strays [on <target>]` | `adoption list-adoptable [--on <target>]` | **NEW**: List adoptable containers | **NEW COMMAND** |
| `adopt <name> [on <target>]` | `adoption adopt --name <name> [--on <target>]` | **NEW**: Adopt container into garden | **NEW COMMAND** |
| `adopted [on <target>]` | `adoption list-adopted [--on <target>]` | **NEW**: List adopted services | **NEW COMMAND** |
| `release <name> [on <target>]` | `adoption unadopt --name <name> [--on <target>]` | **NEW**: Release adopted service | **NEW COMMAND** |
| `borrowed [on <target>]` | `adoption list-borrowed [--on <target>]` | **NEW**: List borrowed services | **NEW COMMAND** |
| `borrow <svc> from <url> [on <target>]` | `adoption borrow --name <svc> --url <url> [--on <target>]` | **NEW**: Register external service | **NEW COMMAND** |
| `return <name> [on <target>]` | `adoption unborrow --name <name> [--on <target>]` | **NEW**: Unregister borrowed service | **NEW COMMAND** |

**Note**: Zen supports both positional `from` and flag `--from`: `borrow redis from redis://cache:6379` or `borrow redis --from redis://cache:6379`

### Garden Observation (UNCHANGED)

| Zen | Normative | Semantics | Changes |
|-----|-----------|-----------|---------|
| `observe [stone]` | `stones list [--name <stone>]` | List stones or show specific stone | None |
| `status [on <target>]` | `stones status [--on <target>]` | Show local stone status | Changed `--at` → `--on` |
| *(not implemented)* | `stones show --name <name>` | Show specific stone details | Planned |
| *(not implemented)* | `metrics get [--on <target>]` | Get stone metrics | API exists |
| `watch [on <target>]` | `events stream [--on <target>]` | Stream events | Changed `--at` → `--on` |
| `watch offering <name> logs [on <target>]` | `services logs --name <name> [--on <target>]` | Stream service logs | Changed `at` → `on` |

### Templates (UNCHANGED)

| Zen | Normative | Semantics | Changes |
|-----|-----------|-----------|---------|
| `template list [on <target>]` | `templates list [--on <target>]` | List offering templates | Changed `--at` → `--on` |
| `template show <name> [on <target>]` | `templates show --name <name> [--on <target>]` | Show template content | Changed `at` → `on` |

### Inventory (REVISED)

| Zen | Normative | Semantics | Changes |
|-----|-----------|-----------|---------|
| `reconcile [--drop-invalid] [on <target>]` | `services reconcile [--drop-invalid] [--on <target>]` | Reconcile inventory | Changed `--at` → `--on` |
| *(proposed: heal)* | `offerings heal [--drop-invalid] [--on <target>]` | Heal garden (zen alias for reconcile) | Zen alternative |

### Pond Security (REVISED)

| Zen | Normative | Semantics | Changes |
|-----|-----------|-----------|---------|
| `place keystone [--passphrase <p>] [on <target>]` | `pond init [--passphrase <p>] [--on <target>]` | Initialize pond | Changed `--at` → `--on` |
| *(not implemented)* | `pond status [--on <target>]` | Show pond status | API exists |
| `invite [on <target>]` | `pond invite [--on <target>]` | Generate invitation code | Changed `--at` → `--on` |
| `place stone --code <c> [on <target>]` | `pond join --code <c> [--on <target>]` | Join pond | Changed `--at` → `--on` |
| `lift keystone [on <target>]` | `pond remove [--on <target>]` | Destroy entire pond | **NEW**: Keystone removal (pond collapse) |
| `lift stone <name> [on <target>]` | `pond untrust --name <name> [--on <target>]` | Remove stone from pond | Changed `at` → `on` |

### Stone Operations (REVISED)

| Zen | Normative | Semantics | Changes |
|-----|-----------|-----------|---------|
| `refresh <component> --from <path> [on <target>]` | `stones upgrade --component <c> --from <path> [--on <target>]` | Upgrade binary | Changed `--at` → `--on` |
| *(not implemented)* | `stones shutdown [--on <target>]` | Shutdown stone | API exists |
| `take-root [on <target>]` | `stones install-service [--on <target>]` | Install as system service | Changed `at` → `on` (zen positional) |

### Console Control (UNCHANGED BUT NOTED)

| Zen | Normative | Semantics | Changes |
|-----|-----------|-----------|---------|
| `make stone sing [--forever] [on <target>]` | `console set-mode --mode verbose [--persist] [--on <target>]` | Set verbose output | Changed `--at` → `--on` |
| `make stone quiet [on <target>]` | `console set-mode --mode informative [--on <target>]` | Set informative output | Changed `--at` → `--on` |
| `make stone silent [on <target>]` | `console set-mode --mode silent [--on <target>]` | Set silent output | Changed `--at` → `--on` |
| `make stone minimal [on <target>]` | `console set-mode --mode minimal [--on <target>]` | Set minimal output | Changed `--at` → `--on` |
| *(not implemented)* | `console get-mode [--on <target>]` | Get current mode | API exists |

**Note**: "make stone <mode>" is marked for user testing. May be simplified in future.

### Context Management (UNCHANGED)

| Zen | Normative | Semantics | Changes |
|-----|-----------|-----------|---------|
| `tend [--verbose]` | `context show [--verbose]` | Show tending context | None |
| `tend <target>` | `context set --target <target>` | Set tending context | None |
| `tend --clear` | `context clear` | Clear tending context | None |

### Ceremonies (NEW)

| Zen | Normative | Semantics | Changes |
|-----|-----------|-----------|---------|
| `ceremonies` | `ceremonies list` | **NEW**: List available ceremonies | **NEW COMMAND** |
| `ceremony <name>` | `ceremonies start --name <name>` | **NEW**: Run guided ceremony | **NEW COMMAND** |

### Discovery (PROPOSED - Not Yet Implemented)

| Zen | Normative | Semantics | Changes |
|-----|-----------|-----------|---------|
| `explore` | `stones discover` | Discover stones on network | Proposed |
| `check <stone>` | `stones health-check --name <stone>` | Check stone health | Proposed |

---

## Key Syntax Changes Summary

### Breaking Changes from Current Implementation
1. **`at` → `on`**: All stone targeting now uses `on` keyword
   - Old: `offer mongodb at stone-02`
   - New: `offer mongodb on stone-02`
   - Migration: `@` accepted as alias for backwards compatibility

2. **New verbs added**:
   - `uproot` - hard delete (destroys container)
   - `find strays` - list adoptable containers
   - `adopt` - adopt container
   - `release` - release adopted service
   - `borrow` - register external service
   - `return` - unregister borrowed service
   - `ceremony` - run guided workflows

3. **Semantic clarifications**:
   - `remove` = soft delete (registry only, container preserved)
   - `uproot` = hard delete (container destroyed)
   - `delete` (normative) = soft delete
   - `destroy` (normative) = hard delete

### Non-Breaking Enhancements
1. `@` symbol supported as shorthand for `on`
2. Both positional and flag syntax for `from` in borrow command
3. `--at` flag still supported in normative (but `--on` preferred)

---

**Status**: Ready for specification phase
**Contributors**: Chen, Rodriguez, Tanaka, Thompson, Sharma, O'Connor
**Date**: 2026-01-21
**Revision**: Added Part 7 (Radical Redesign Proposals) + Complete Revised Mapping
