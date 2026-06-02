# MEDIA-0009: Format Negotiation Contract

**Status**: Accepted
**Date**: 2026-06-01

Extends MEDIA-0005 (kind admission via `EncoderAccepts`), MEDIA-0007 (cache-as-storage keying), and MEDIA-0008 (streaming encoders). Closes the "every consumer hard-codes the output format" gap that the prior ADRs left intact: encoders today declare what they admit but never what they produce, and recipes declare a single pinned format or "preserve source" with no middle ground. Adding AVIF, animated WebP-as-static-fallback, or future video output forces a touch on every recipe call site instead of one encoder registration plus one recipe edit.

## Contract

| Aspect | Specification |
|---|---|
| **Inputs** | A resolved `MediaRecipe` (with optional `AllowedOutputFormats` allowlist), the request's `Accept` HTTP header (possibly absent), and the encoder registry (each encoder declares `Accepts: KindSet`, `OutputFormat: string`, `PreservesAnimation: bool`). |
| **Outputs** | A single negotiated format slug (`"webp"`, `"jpeg"`, `"avif"`, ...) and the matching `Content-Type` MIME string. The negotiated format is folded into the recipe fingerprint so MEDIA-0007 produces a distinct cache entry per output format. `Vary: Accept` is emitted on the response whenever the recipe's allowlist admits more than one format and no format-shortcut URL pinned the choice. |
| **Error modes** | `NoCompatibleEncoderException` when `recipe.AllowedOutputFormats ∩ encoder.OutputFormat ∩ Accept-acceptable = ∅` and no recipe-default fallback is admissible. The intersection is evaluated against the source kind (`encoder.Accepts` must contain the source's `MediaKind`); an encoder that produces AVIF but doesn't accept `AnimatedRaster` is silently filtered for an animated source rather than misrouted. Malformed `Accept` headers degrade to "no Accept header" rather than throwing — the network's input is never trusted to be well-formed. |
| **Success criteria** | (1) Adding AVIF support to PackageCard is a one-line change to the recipe and a one-encoder registration; zero churn in `MediaController`, the planner, or any consumer. (2) The same `(source, recipe)` pair produces distinct cache entries per negotiated format with no key collisions. (3) `Vary: Accept` is present iff the response could legitimately differ on Accept and absent iff the format was pinned by recipe-with-single-allowed-format or by format-shortcut URL. (4) Format-shortcut URLs (`/media/{id}/webp`) bypass negotiation entirely — operator override always wins. |

## Context

The framework today picks the output format through two narrow paths:

1. **Recipe pins it.** A recipe like PackageCard calls `.EncodeAs("webp", 82)`, which materialises an `EncodeStep` with `Format = "webp"`. `MediaController.RenderAsync` notices the pinned format (`formatPinned = effectiveRecipe.Steps.OfType<EncodeStep>().Any(e => e.Format is not null)`) and suppresses `Vary: Accept`. The output format is whatever the recipe author wrote, full stop.

2. **Format-shortcut URL.** A request to `/media/{id}/png` synthesises an ad-hoc recipe via `MediaRecipeRegistry.TryResolve` (`MediaRecipe.New().EncodeAs("png", Quality.Web)`). The operator's URL choice wins; the registered recipe is bypassed.

Both paths are operator-driven and explicit. Neither admits client preference. Three concrete pains have surfaced as the catalog grows:

1. **No way to declare "this recipe can serve WebP or AVIF, pick the best one the client accepts."** Adding AVIF to PackageCard today means either (a) renaming the existing recipe and registering a parallel `package-card-avif` and burning the choice into every URL the UI generates, or (b) parsing `Accept` ad-hoc inside the controller and switching the recipe out from under itself — which breaks the cache key (MEDIA-0007 hashes the recipe, not the response, so two formats would collide on the same cache entry and poison each other).

2. **Consumers hard-code modern formats.** The Gposingway UI knows that PackageCard serves WebP and hard-codes `<img src=".../media/{id}/recipe/package-card">` with no `<picture>` fallback. A Safari 14 client that doesn't speak WebP gets a broken image. The recipe should declare what it can serve and let the controller resolve the right one per-request; the consumer shouldn't have to know.

3. **AVIF adoption requires touching every call site.** With no allowlist primitive, "make PackageCard, PackageSighting, ArticleCard, and ArticleHero serve AVIF where the client supports it" is a four-recipe rewrite plus a controller fork plus a cache-key migration. With the allowlist primitive, it's four `AllowedOutputFormats = [..., "avif"]` edits and one AVIF encoder registration. The leverage ratio is the whole point of this ADR.

The surface that already exists is right-shaped for this. MEDIA-0005's `EncoderAccepts` table is a declarative encoder-capability registry; it just needs an `OutputFormat` column. MEDIA-0007's cache fingerprint is content-addressed and already includes the recipe; it just needs the negotiated format folded in. MEDIA-0008's streaming encoder path doesn't care what format flows through it. The negotiation is a pure resolver — `(recipe, accept, registry) → format` — slotted in front of `EncoderSelector.For` with no downstream contract changes.

## Decision

Six pieces, all declarative; the resolver is the only piece with logic.

### a. `Encoder.OutputFormat` (declarative)

`EncoderAccepts` widens from `{ slug → KindSet }` to `{ slug → (Accepts: KindSet, OutputFormat: string, PreservesAnimation: bool) }`. The `slug` and `OutputFormat` are equal today (the `"webp"` encoder produces `"webp"`); the column is explicit so a future "transcoder" encoder could declare `Accepts: Video, OutputFormat: "mp4"` and slot into the same registry. `PreservesAnimation` is `true` for the WebP, GIF, APNG encoders and `false` for JPEG, BMP, TIFF. The resolver uses it to filter the encoder set when the source is animated and the recipe hasn't explicitly opted into a `FlattenTo` step (per MEDIA-0005's kind-admission rules).

No generics. No type parameters. The table is a flat `IReadOnlyDictionary` and stays one for grep-ability.

### b. `MediaRecipe.AllowedOutputFormats` (declarative)

`MediaRecipe` gains:

```csharp
public ImmutableArray<string>? AllowedOutputFormats { get; init; }
```

Nullable. `null` (the default) means "preserve source format" — exactly the current behavior, unchanged. A non-null value declares the explicit allowlist of format slugs this recipe is willing to emit; the first entry is the recipe's preferred default when the Accept header offers no overlap. Adding AVIF to a recipe means appending `"avif"` to the array.

The builder gains `.AllowOutputFormats(params string[] slugs)`. Format-shortcut recipes synthesised by `MediaRecipeRegistry.TryResolve` leave `AllowedOutputFormats = null` and rely on their pinned `EncodeStep.Format` — the shortcut's contract is "operator pinned it, negotiation doesn't apply," and `null` is the most direct expression of that.

### c. Accept header parsing (RFC 9110 q-values)

A new internal `AcceptHeaderParser.Parse(string?) → IReadOnlyList<AcceptEntry>` reads the header into ranked entries:

```csharp
public readonly record struct AcceptEntry(string Type, string Subtype, double Q, int OrderIndex);
```

Rules per RFC 9110 §12.5.1:

- Missing or empty header degrades to a single `*/*` entry at `q=1.0`. A malformed token (no `/`, garbage q-value, anything that doesn't lex) is skipped silently; if every token is malformed, the result is the same `*/*` fallback. The network is never trusted to send valid input.
- `q` defaults to `1.0` when omitted. Values outside `[0, 1]` are clamped; `q=0` means "explicitly unacceptable" and the entry is dropped rather than retained as a negative preference.
- Entries are sorted by `(Q descending, specificity descending, OrderIndex ascending)`. Specificity is a three-tier integer: exact `type/subtype` = 3, `type/*` = 2, `*/*` = 1.
- Parameters past `q` (`;v=1`, `;profile=…`) are parsed and ignored for matching. They are not part of the negotiated identity; the cache key includes only the resolved format slug.

The parser is allocation-light (a single `Span<char>` walk, one `List<AcceptEntry>` for the result) and pure. Tested in isolation — no controller harness needed.

### d. Negotiation algorithm (intersection + ranking)

The resolver lives at `MediaController.RenderAsync` (or a private helper invoked from it; the placement is incidental). Pseudocode:

```csharp
string NegotiateFormat(
    MediaRecipe recipe,
    string? acceptHeader,
    MediaKind sourceKind,
    bool sourceIsAnimated,
    IReadOnlyDictionary<string, EncoderEntry> registry)
{
    // 1. Recipe with no allowlist preserves source format. MEDIA-0005 rules apply.
    if (recipe.AllowedOutputFormats is null)
        return PreserveSourceFormat(sourceKind);

    // 2. Filter encoder registry by what can actually encode this source.
    var producible = recipe.AllowedOutputFormats
        .Where(slug => registry.TryGetValue(slug, out var enc)
                    && enc.Accepts.Contains(sourceKind)
                    && (!sourceIsAnimated || enc.PreservesAnimation))
        .ToList();

    if (producible.Count == 0)
        throw new NoCompatibleEncoderException(recipe.Name, sourceKind, sourceIsAnimated);

    // 3. Parse Accept; degrades to */* if missing or malformed.
    var accepted = AcceptHeaderParser.Parse(acceptHeader);

    // 4. For each Accept entry in q-then-specificity order, return first allowlist match.
    foreach (var entry in accepted)
    {
        foreach (var slug in producible)
        {
            var enc = registry[slug];
            if (Matches(entry, enc.MimeType))
                return slug;
        }
    }

    // 5. No Accept overlap. Recipe's first allowed format is the default.
    return producible[0];
}

bool Matches(AcceptEntry entry, string mime)
{
    var (t, s) = SplitMime(mime); // "image/webp" -> ("image", "webp")
    if (entry.Type == "*" && entry.Subtype == "*") return true;
    if (entry.Type == t && entry.Subtype == "*") return true;
    return entry.Type == t && entry.Subtype == s;
}
```

The algorithm is greedy on q-rank: the highest-q match wins, ties broken by specificity, then by recipe order. There is no scoring function, no weighted optimisation, no "pick the smallest-byte format" heuristic — the recipe author already declared the preference order in `AllowedOutputFormats`, and that ordering breaks ties.

`PreserveSourceFormat` returns the existing MEDIA-0005 result: source kind maps to the encoder that admits it, defaulting to the source's own format slug.

### e. Cache key incorporation (recipe fingerprint includes negotiated format)

MEDIA-0007 fingerprints the recipe as part of the cache key. The fingerprint extends to include the negotiated format slug:

```
fingerprint = SHA256(recipe_canonical_json ‖ "::fmt=" ‖ negotiated_format)
```

The `::fmt=` separator is a literal, not a length-prefixed encoding — the slug character set is `[a-z0-9]` (per the format-slug grammar already in `EncoderSelector.CanonicalSlug`) so there is no ambiguity. The same `(source, recipe)` pair with `Accept: image/avif` and `Accept: image/webp` produces two distinct cache entries; neither can poison the other. The cache grows by a multiplicative factor equal to the count of formats actually negotiated in the wild — for a typical 2-format allowlist (`["webp", "avif"]`) that's a 2× ceiling, not an explosion.

The format-shortcut path stays unchanged: its synthesised recipe has a pinned `EncodeStep.Format`, the negotiation resolver short-circuits to that slug, and the fingerprint folds the same slug in. A `/media/{id}/webp` request and a `/media/{id}/recipe/package-card` request that negotiates to WebP produce different fingerprints because the recipes are different — that's MEDIA-0007's existing invariant, not a regression here.

### f. `Vary: Accept` correctness

The controller currently emits `Vary: Accept` when the recipe has no pinned `EncodeStep.Format` and no `FlattenToStep`. The new rule is more precise:

- `recipe.AllowedOutputFormats is null` and no pinned format → emit `Vary: Accept` (source-preserve mode can still differ per kind).
- `recipe.AllowedOutputFormats is null` and pinned format → no `Vary` (today's behavior, unchanged).
- `recipe.AllowedOutputFormats.Length == 1` → no `Vary` (only one possible output, Accept can't change it).
- `recipe.AllowedOutputFormats.Length > 1` → emit `Vary: Accept` (the allowlist admits multiple formats; Accept does choose).
- Format-shortcut URL → no `Vary` (the URL pinned it).

The fourth case is the new one. The allowlist genuinely makes the response Accept-dependent and downstream caches must vary on it; suppressing `Vary` here would let a Chrome client's AVIF and a Safari 14 client's WebP collide in a shared cache layer.

## Migration

- **Recipes without `AllowedOutputFormats`**: behavior unchanged. Source format is preserved exactly as today. No migration step required.
- **Recipes opting in to negotiation**: add `.AllowOutputFormats("webp", "avif")` (or equivalent) to the recipe builder. The first slug is the fallback default when Accept offers no overlap.
- **Format-shortcut URLs (`/media/{id}/webp`, etc.)**: unchanged. The synthesised recipe carries the pinned `EncodeStep.Format` and bypasses negotiation.
- **Gposingway recipe migration** (per the recon survey):
  - `PackageCard`, `PackageSighting`, `ArticleCard`: `AllowOutputFormats("webp", "avif")` — prefers WebP until the AVIF encoder is wired, at which point clients sending `Accept: image/avif,image/webp` will receive AVIF without further recipe edits.
  - `PackageHero`, `ArticleHero`: same allowlist (`"webp", "avif"`), same trajectory. The 15-20% byte savings on the hero assets is where AVIF's value concentrates.
  - `AdminPreview`: no allowlist (preserves source format). Editor UX prioritises seeing the source as authored; bandwidth is not the constraint on an admin surface.
- **Cache invalidation**: not required. The fingerprint change adds the `::fmt=` suffix; existing cached entries (which were keyed under the pre-ADR fingerprint) will not be hit by post-ADR requests and will age out naturally per the cache TTL policy. No backfill, no bulk delete.

## What we explicitly DON'T do

- **No automatic transcoding chain at write time.** The encoder produces a single format per render. There is no "encode the source once as JPEG, WebP, and AVIF then fan them into the cache" path. Each cache entry is one negotiated format; subsequent requests for a different format trigger a separate render. The complexity of orchestrating multi-format derivation belongs to a future ADR, not this one.
- **No Accept-CH (client hints) negotiation.** `DPR`, `Sec-CH-DPR`, `Sec-CH-Viewport-Width` are out of scope. The framework negotiates on `Accept` only; viewport-driven dimension selection is a recipe-input concern, not a format-output concern.
- **No SVG-as-output.** SVG is input-only per MEDIA-0006. A recipe cannot declare `AllowedOutputFormats = ["svg"]`; the resolver would throw `NoCompatibleEncoderException` because no encoder registers `OutputFormat: "svg"`. This is enforced by the registry, not by special-casing in the resolver.
- **No content-length-based format selection.** The resolver does not encode the source twice to compare byte sizes and pick the smaller one. The recipe author's preference order in `AllowedOutputFormats` is authoritative; runtime optimisation across encodings is a different problem (and probably belongs to an offline derivation job, not to a hot request path).

## Addendum (2026-06-01): producibility is the single source of truth

The algorithm in §d filters the allowlist to `producible` — slugs the encoder registry can actually emit (line 91–95). The original implementation took a shortcut: it filtered only on `EncoderAccepts.MediaTypeFor(slug) is null`, trusting that every slug in `EncoderAccepts` was producible. That trust was misplaced. `EncoderAccepts` was widened to **declare** `avif` (with a MIME type and `Raster` admission) *before* `EncoderSelector` had a concrete AVIF encoder — directly contradicting the "Out of scope" note that the encoder binding lands *after* the table is widened. The result was the classic two-tables-no-source-of-truth split-brain (cf. DATA-0098):

- **Capability table** (`EncoderAccepts._descriptors`) said avif is a real encoder.
- **Producer** (`EncoderSelector.For`) threw `NotSupportedException` for avif.

So the negotiator happily picked avif for a Chrome client sending `Accept: image/avif`, then `EncoderSelector.For("avif")` 500'd. The tests even enforced the divergence — `EncoderDescriptorMatrixSpec` asserted avif *is* registered and never cross-checked it against the producer.

**Resolution — derive the capability from the producer, in three places, all gated by `EncoderSelector.SupportedFormats` (the single producibility authority):**

1. **`EncoderAccepts.All` (and every lookup the negotiator/planner uses) is now the LIVE registry** — `_descriptors ∩ EncoderSelector.SupportedFormats`. A declared-but-unproducible format (avif until its encoder is wired) is filtered out, so `MediaTypeFor("avif")` is `null` and the negotiator's existing null-skip turns into the fallthrough §d already promised. The avif descriptor stays in `_descriptors` as forward-compat metadata and goes live automatically the moment `EncoderSelector` can produce it.
2. **The no-overlap / no-Accept fallback** in `FormatNegotiator` no longer returns `recipeAllowedFormats[0]` blind — it returns the first *producible* allowlist entry (`PreferredProducible`), degrading to source format when the whole allowlist is non-producible. A recipe that lists avif first can no longer 500 on a no-overlap request.
3. **Format-shortcut resolution** (`MediaRecipeRegistry.TryResolve`) gates synthesis by producibility: `?recipe=avif` resolves as *unknown* (honest 404) instead of synthesising an avif-pinned recipe that 500s. avif stays a *reserved name* (no host recipe may claim it) for forward-compat, but is not an advertised/resolvable shortcut until producible. `FormatShortcuts` advertises only producible formats.

A conformance test (`EncoderDescriptorMatrixSpec.Every_advertised_encoder_is_producible_by_the_selector`) now asserts `EncoderAccepts.All ⊆ EncoderSelector` producibility — the cross-check that was missing. Wiring a real AVIF encoder is still the one-line `EncoderSelector` change §0/§191 describes; doing so flips the existing avif descriptor live with zero recipe or consumer churn.

**Fourth path — closed (boot-time recipe validation):** a host config/code recipe that *explicitly* pins `EncodeAs("avif")` (or any non-producible slug) bypasses both the negotiator and the shortcut gate — the planner's terminal gate is deliberately permissive for unknown slugs, deferring to `EncoderSelector.For` which throws. This now fails fast at boot instead of per-request: `RecipeOutputFormatValidator.EnsureProducible` walks every built recipe's `EncodeStep`/`FlattenToStep` and rejects any non-producible `Format` with a `MediaRecipeBindingException` naming the format and listing the producible set. It runs on **both** boot paths — `ConfiguredRecipeBinder` (config/appsettings recipes) and `MediaRecipeRegistry.DiscoverCodeRecipes` (`[MediaRecipe]` code recipes) — so the source of the recipe is irrelevant. To avoid introducing a *second* canonicalizer (which would be a fresh split-brain), the `jpg`→`jpeg` alias folding now lives in exactly one place, `EncoderSelector.CanonicalizeSlug`, and `EncoderSelector.For` consults it too — so `EncodeAs("jpg")` validates *and* produces end-to-end (it previously bound clean and 500'd on the switch default). `EncoderSelector.CanProduce(slug)` is the single producibility predicate the registry shortcut resolver and both validators share.

## Consequences

**Positive:**

- Adding a new output format is a one-encoder registration plus targeted recipe-allowlist edits. No churn in `MediaController`, the planner, the cache key, or any consumer of the existing surface.
- Recipes become honest about what they serve. A recipe that lists `["webp", "avif", "jpeg"]` documents the full Accept-side contract; UI authors can read it without spelunking the encoder.
- The cache stays correct under negotiation. The fingerprint change is mechanical and forces per-format entries by construction; no human-correctness step required.
- `Vary: Accept` is now precise rather than over-broad, which lets downstream caches (CDN, browser, intermediate proxy) make better decisions about whether two requests share a cache entry.

**Negative:**

- Accept header parsing is one more code path that has to handle malformed input gracefully. The fail-closed-to-`*/*` rule shrinks the blast radius, and the parser is testable in isolation, but it's still net new surface.
- Cache keys grow by ~5 bytes (the `::fmt=` prefix plus the slug) per entry. At the catalog's scale this is negligible — a million entries gains ~5 MB across all entries' fingerprints combined.
- Recipes opting in must explicitly list AVIF (or any new format) to benefit. There is no "opt-in to all modern formats" wildcard; the allowlist is explicit by design (auditability, predictability), at the cost of one recipe edit per format-recipe pair.
- A cache entry per `(source, recipe, format)` triple multiplies the storage ceiling. The mitigation is that real negotiation outcomes are bounded — a 2-format allowlist with realistic browser populations produces close to a 2× multiplier, not a Cartesian explosion.

## Out of scope

- The AVIF encoder binding itself. This ADR establishes the negotiation contract; registering the AVIF encoder is a follow-up commit once the framework's `EncoderAccepts` table is widened to include AVIF input as `Raster` (and eventually `AnimatedRaster`, once the underlying library's animated-AVIF support is stable enough to admit).
- Accept-CH (client hints) negotiation. Different signal, different headers, different cache semantics.
- Image-format detection from response body (sniffing). The format slug is authoritative; the response's `Content-Type` is set from the slug, not inferred from the bytes.
- Per-format quality tuning beyond what's already in the recipe. A recipe that declares `.AllowOutputFormats("webp", "avif")` and `.EncodeAs("webp", 82)` will encode AVIF at the same nominal quality (82); per-format quality requires extending `EncodeStep` and is left for a follow-up if the visual-quality calibration diverges between formats meaningfully.

## Test coverage requirements

The negotiation contract is testable in isolation from the controller (parser + resolver are pure functions) and end-to-end through `MediaController` for the cache-and-Vary integration.

- **Q-value parsing:** Accept headers with explicit q-values (`image/webp;q=0.9`), default q (`image/webp`), q=0 (drops the entry), out-of-range q (clamped), malformed q (skipped), missing header (defaults to `*/*`), entirely-malformed header (defaults to `*/*`).
- **Specificity ordering:** `image/webp` outranks `image/*` outranks `*/*` at equal q; equal specificity falls back to header order.
- **Intersection algorithm:** allowlist ∩ encoder-produces ∩ Accept-acceptable. Cover: allowlist with single match, allowlist with multiple matches across q-tiers, allowlist with no overlap (fallback to first allowlist entry), allowlist with no encoder support (throws `NoCompatibleEncoderException`).
- **Recipe-without-allowlist fallback:** source-preserve mode is unchanged from pre-ADR behavior. Encoder is selected via the existing MEDIA-0005 path; `Vary: Accept` is emitted iff format isn't otherwise pinned.
- **No-Accept-header fallback:** missing header resolves to the recipe's first allowed format.
- **Format-shortcut override:** `/media/{id}/webp` with `Accept: image/avif` still emits WebP. Operator override is authoritative.
- **Cache key per-format:** `(source, recipe)` with `Accept: image/avif` and `Accept: image/webp` produce two distinct cache entries; verify both fingerprints and the absence of cross-talk.
- **`Vary: Accept` correctness:** present for multi-format allowlists, absent for single-format allowlists, absent for pinned formats, absent for format-shortcut URLs, present for source-preserve mode when no other pin applies.
- **Animated-source filtering:** an animated source against an allowlist of `["jpeg", "webp"]` filters JPEG out (it doesn't preserve animation) and resolves to WebP regardless of Accept order; if the allowlist is `["jpeg"]` only and the source is animated, the resolver throws `NoCompatibleEncoderException` rather than silently flattening (flattening is an explicit `FlattenToStep` concern per MEDIA-0005).
- **Kind mismatch:** an `AllowedOutputFormats` slug whose encoder doesn't admit the source's `MediaKind` is filtered out before Accept matching; if filtering leaves the producible set empty, the resolver throws.
