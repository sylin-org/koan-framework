# Results — test01 staged composite · codex-oss-qwen38-27b-max · koan arm (attempt 1)

- Harness: codex-cli 0.150.0 over Ollama, model `local-code-candidate:qwen38-27b-q4-max`,
  loaded 100% GPU (20 GB into 24 GB VRAM, 98K context); cap 45 min; per-run overrides only
- Treatment: prompt v3 arm line (koan skill pointer) + SKILL.md v4

| Stage | Battery | Wall clock | Outcome |
|---|---|---|---|
| 1 | 0/1 (no csproj produced; code/ empty) | 2701 s (cap hit) | sustained loop, no artifact |

## Finding

The `max` tuning behaves differently from `daily` and better in one dimension: it **sustained
the agentic loop for the entire 45-minute budget** (events grew steadily throughout) and its
research was genuine — it independently discovered that `EntityController<T>` exposes PATCH
rather than PUT and was verifying the governed path to add a PUT delegator, which is exactly the
seam the successful frontier run subclassed. But it never committed a file: zero writes, empty
workspace at cap.

Combined with `daily` (ends turns early during research) and the opencode cells (tool calls
dropped), the local-tier picture is now:

| Model / pipe | Loop sustainment | Tool execution | Artifact produced |
|---|---|---|---|
| qwen35-9b · opencode | no | no | none |
| qwen38-27b daily · opencode | no | dropped on real tasks | none |
| qwen38-27b daily · codex-OSS | short turns | yes | none |
| qwen38-27b max · codex-OSS | **yes, to cap** | yes | **none** |

**Verdict: the local tier (qwen38-27b, both tunings, GPU-resident) cannot yet produce this
application through any tested pipe.** The failure moved from tool transport (fixed) through
loop sustainment (varies by tuning) to *research-without-commitment* — the model verifies
indefinitely and never crosses into writing. This is recorded as the matrix's local-tier floor.
Promising observation for a future attempt: the max tuning's behavior suggests a nudge-tolerant
model exists here; a continuation-tolerant harness (auto-continuing turns until a file appears)
or a direct "scaffold from the one-block skeleton now, verify after" instruction variant are the
next levers — but as prompts must stay byte-identical across arms, those levers belong to the
harness, not the prompt.
