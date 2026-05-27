using System.Globalization;
using Koan.Media.Abstractions.Recipes;

namespace Koan.Media.Web.Routing;

/// <summary>
/// Stateless parser that turns a query-string parameter dictionary
/// (plus an optional recipe seed) into an effective <see cref="MediaRecipe"/>.
/// Per MEDIA-0004 §8:
/// <list type="number">
///   <item>Seed → either a registered recipe or a format shortcut</item>
///   <item>Query params layer on top via the recipe's <see cref="MutatorKind"/> allowlist</item>
///   <item>Unprefixed mutators target the primary step of their kind</item>
///   <item>Named-step prefix (<c>?stepName.param=</c>) targets that named step</item>
/// </list>
/// All parameter names are lowercased; alias resolution happens during the parse
/// pass (<c>w</c>→<c>width</c>, <c>h</c>→<c>height</c>, <c>q</c>→<c>quality</c>, <c>f</c>→<c>format</c>).
/// </summary>
public static class MediaUrlParser
{
    /// <summary>Recognised aliases. Canonical names appear as values.</summary>
    public static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["w"] = "width",
            ["h"] = "height",
            ["q"] = "quality",
            ["f"] = "format",
            ["aspect"] = "crop",
        };

    /// <summary>Outcome of a parse pass.</summary>
    public sealed record ParseResult(
        MediaRecipe Recipe,
        IReadOnlyList<string> IgnoredParams,
        IReadOnlyList<string> RejectedParams)
    {
        public bool HasRejections => RejectedParams.Count > 0;
    }

    /// <summary>
    /// Build an effective recipe for a single request. <paramref name="seedRecipe"/> may be null
    /// (pure ad-hoc) or a registered recipe or a format-shortcut recipe. <paramref name="strict"/>
    /// controls how unknown params are handled: when true, they go into <c>RejectedParams</c>
    /// (controller should 400). When false, they go into <c>IgnoredParams</c> (controller surfaces
    /// via <c>X-Koan-Media-IgnoredParams</c>).
    /// </summary>
    public static ParseResult Parse(
        MediaRecipe? seedRecipe,
        IDictionary<string, string> queryParams,
        bool adHocAllowed = true,
        bool strict = false)
    {
        var normalised = NormaliseParams(queryParams);
        var ignored = new List<string>();
        var rejected = new List<string>();

        MediaRecipeBuilder builder;
        MutatorKind allowed;
        bool isAdHoc = false;

        if (seedRecipe is null)
        {
            if (!adHocAllowed && normalised.Count > 0)
            {
                rejected.AddRange(normalised.Keys);
                return new ParseResult(
                    MediaRecipe.New().PreserveFormat().Build(),
                    ignored, rejected);
            }
            builder = MediaRecipe.New();
            allowed = MutatorKind.All;
            isAdHoc = true;
        }
        else
        {
            // Seed clone: rebuild from the recipe's existing steps
            builder = MediaRecipe.New()
                .WithName(seedRecipe.Name ?? "ad-hoc")
                .WithVersion(seedRecipe.Version);
            if (seedRecipe.Description is { Length: > 0 } d) builder.WithDescription(d);
            allowed = seedRecipe.AllowedMutators;
            foreach (var step in seedRecipe.Steps)
            {
                CopyStep(builder, step);
            }
        }

        // Pre-pass: extract overlay layers (own grammar — overlay.N.field — not a step name)
        var overlayLayers = ExtractOverlayLayers(normalised, ignored, rejected, strict);

        // Pass 1: collect step-name-prefixed overrides keyed by step name
        var byStepName = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var unprefixed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in normalised)
        {
            var dot = key.IndexOf('.', StringComparison.Ordinal);
            if (dot > 0 && dot < key.Length - 1)
            {
                var stepName = key[..dot];
                var param = key[(dot + 1)..];
                if (!byStepName.TryGetValue(stepName, out var bag))
                    byStepName[stepName] = bag = new(StringComparer.OrdinalIgnoreCase);
                bag[param] = value;
            }
            else
            {
                unprefixed[key] = value;
            }
        }

        // For ad-hoc requests, all params are appended as fresh steps
        if (isAdHoc)
        {
            ApplyAdHoc(builder, unprefixed, ignored, rejected, strict);
            ApplyOverlayLayers(builder, overlayLayers, mutatorAllowed: true);
            return new ParseResult(builder.Build(), ignored, rejected);
        }

        // Recipe overrides: apply by mutator allowlist + named-step targeting
        ApplyRecipeOverrides(builder, seedRecipe!, unprefixed, allowed, ignored, rejected, strict);
        // Named-step overrides — same allowlist rules
        foreach (var (stepName, bag) in byStepName)
        {
            ApplyNamedStep(builder, seedRecipe!, stepName, bag, allowed, ignored, rejected, strict);
        }
        // Overlay overrides — only when the recipe declares MutatorKind.Overlay
        var overlayAllowed = allowed.HasFlag(MutatorKind.Overlay);
        if (overlayLayers.Count > 0 && !overlayAllowed)
        {
            foreach (var (key, value) in EnumerateRejectedOverlayParams(overlayLayers))
            {
                Reject(new Dictionary<string, string>(), ignored, rejected, strict, key, value);
            }
        }
        else
        {
            ApplyOverlayLayers(builder, overlayLayers, mutatorAllowed: overlayAllowed);
        }

        return new ParseResult(builder.Build(), ignored, rejected);
    }

    private static void CopyStep(MediaRecipeBuilder b, MediaStep step)
    {
        switch (step)
        {
            case AutoOrientStep ao:
                b.AutoOrient(ao.Keep);
                break;
            case ExtractFrameStep ef:
                b.ExtractFrame(ef.Index);
                break;
            case RotateStep rs:
                b.Rotate(rs.Degrees);
                break;
            case FlipStep fs:
                if (fs.Axis == FlipAxis.Horizontal) b.FlipHorizontal(); else b.FlipVertical();
                break;
            case ShapeStep ss:
                b.Shape(crop: ss.Crop, fit: ss.Fit, position: ss.Position, bg: ss.Background);
                break;
            case ResizeStep rz:
                b.Resize(rz.Width, rz.Height, rz.Dpr);
                break;
            case OverlayStep os:
                foreach (var layer in os.Layers) b.Overlay(layer);
                break;
            case StripStep st:
                b.Strip(st.Kinds);
                break;
            case EncodeStep es:
                if (es.Format is null) b.PreserveFormat(es.Quality);
                else b.EncodeAs(es.Format, es.Quality);
                break;
            case FlattenToStep ft:
                b.FlattenTo(ft.Format, ft.Quality);
                break;
        }
        if (step.Name is { Length: > 0 }) b.Name(step.Name);
        if (step.Primary) b.Primary();
    }

    private static Dictionary<string, string> NormaliseParams(IDictionary<string, string> raw)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in raw)
        {
            if (string.IsNullOrWhiteSpace(k)) continue;
            // Apply aliases only on simple keys (no dot prefix)
            var key = k.Trim().ToLowerInvariant();
            if (!key.Contains('.') && Aliases.TryGetValue(key, out var canon))
                key = canon;
            result[key] = v?.Trim() ?? "";
        }
        return result;
    }

    private static void ApplyAdHoc(
        MediaRecipeBuilder b,
        IDictionary<string, string> p,
        List<string> ignored,
        List<string> rejected,
        bool strict)
    {
        // Crop / aspect (alias already resolved to "crop")
        if (Take(p, "crop") is { } cropRaw)
        {
            if (!CropSpec.TryParse(cropRaw, out var spec))
            {
                Reject(p, ignored, rejected, strict, "crop", cropRaw);
            }
            else
            {
                Fit fit = Fit.Cover;
                if (Take(p, "fit") is { } fitRaw && !TryParseFit(fitRaw, out fit))
                {
                    Reject(p, ignored, rejected, strict, "fit", fitRaw);
                }
                Position? pos = null;
                if (Take(p, "position") is { } posRaw)
                {
                    if (!Position.TryParse(posRaw, out var p2)) Reject(p, ignored, rejected, strict, "position", posRaw);
                    else pos = p2;
                }
                Background? bg = null;
                if (Take(p, "bg") is { } bgRaw)
                {
                    if (!Background.TryParse(bgRaw, out var b2)) Reject(p, ignored, rejected, strict, "bg", bgRaw);
                    else bg = b2;
                }
                b.Shape(crop: spec, fit: fit, position: pos, bg: bg);
            }
        }
        else if (p.ContainsKey("position") || p.ContainsKey("fit") || p.ContainsKey("bg"))
        {
            // position/fit/bg without crop/aspect → 400 (silent no-ops mask typos, per ADR)
            foreach (var key in new[] { "position", "fit", "bg" })
            {
                if (Take(p, key) is { } v) Reject(p, ignored, rejected, strict, key, v);
            }
        }

        if (Take(p, "frame") is { } frameRaw)
        {
            if (int.TryParse(frameRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fi))
                b.ExtractFrame(fi);
            else Reject(p, ignored, rejected, strict, "frame", frameRaw);
        }

        if (Take(p, "rotate") is { } rotRaw)
        {
            if (int.TryParse(rotRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var deg))
                b.Rotate(deg);
            else Reject(p, ignored, rejected, strict, "rotate", rotRaw);
        }

        if (Take(p, "flip") is { } flipRaw)
        {
            if (string.Equals(flipRaw, "v", StringComparison.OrdinalIgnoreCase)) b.FlipVertical();
            else if (string.Equals(flipRaw, "h", StringComparison.OrdinalIgnoreCase)) b.FlipHorizontal();
            else Reject(p, ignored, rejected, strict, "flip", flipRaw);
        }

        // Resize
        int? width = TakeInt(p, "width", ignored, rejected, strict);
        int? height = TakeInt(p, "height", ignored, rejected, strict);
        double dpr = 1.0;
        if (Take(p, "dpr") is { } dprRaw)
        {
            if (!double.TryParse(dprRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out dpr))
            {
                Reject(p, ignored, rejected, strict, "dpr", dprRaw);
                dpr = 1.0;
            }
        }
        if (width.HasValue || height.HasValue)
        {
            b.Resize(width, height, dpr);
        }

        // Strip
        if (Take(p, "strip") is { } stripRaw)
        {
            var kinds = ParseStripKinds(stripRaw);
            if (kinds is null) Reject(p, ignored, rejected, strict, "strip", stripRaw);
            else b.Strip(kinds.Value);
        }

        // Encode
        var format = Take(p, "format");
        var quality = Take(p, "quality");
        if (format is not null || quality is not null)
        {
            int q = Quality.Web;
            if (quality is not null && !Quality.TryParse(quality, out q))
            {
                Reject(p, ignored, rejected, strict, "quality", quality);
                q = Quality.Web;
            }
            if (format is null) b.PreserveFormat(q);
            else b.EncodeAs(format.ToLowerInvariant(), q);
        }
        else
        {
            b.PreserveFormat();
        }

        // Anything left in the dictionary is unrecognised
        foreach (var (k, v) in p)
        {
            Reject(new Dictionary<string, string>(), ignored, rejected, strict, k, v);
        }
    }

    private static void ApplyRecipeOverrides(
        MediaRecipeBuilder b,
        MediaRecipe seed,
        IDictionary<string, string> p,
        MutatorKind allowed,
        List<string> ignored,
        List<string> rejected,
        bool strict)
    {
        // The seed steps are already copied into the builder. The mutator
        // semantics replace single-slot stages or mutate primary steps.

        // ------- Dimensions (?w, ?h, ?dpr) target the primary ResizeStep -------
        var hasResizeOverride = p.ContainsKey("width") || p.ContainsKey("height") || p.ContainsKey("dpr");
        if (hasResizeOverride)
        {
            if (!allowed.HasFlag(MutatorKind.Dimensions))
            {
                foreach (var key in new[] { "width", "height", "dpr" })
                    if (Take(p, key) is { } v) Reject(p, ignored, rejected, strict, key, v);
            }
            else
            {
                var primary = seed.FindPrimary<ResizeStep>();
                if (primary is null)
                {
                    // No resize step in seed: append one (treat as ad-hoc append for dimensions only)
                    int? w = TakeInt(p, "width", ignored, rejected, strict);
                    int? h = TakeInt(p, "height", ignored, rejected, strict);
                    double dpr = 1.0;
                    if (Take(p, "dpr") is { } dRaw && double.TryParse(dRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                        dpr = d;
                    if (w.HasValue || h.HasValue) b.Resize(w, h, dpr);
                }
                else
                {
                    int? w = TakeInt(p, "width", ignored, rejected, strict) ?? primary.Width;
                    int? h = TakeInt(p, "height", ignored, rejected, strict) ?? primary.Height;
                    double dpr = primary.Dpr;
                    if (Take(p, "dpr") is { } dRaw && double.TryParse(dRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                        dpr = d;
                    // If only ONE of w/h is given (and the recipe primary had both), proportional scale.
                    var givenW = w.HasValue && (primary.Width != w);
                    var givenH = h.HasValue && (primary.Height != h);
                    if (givenW && !givenH && primary.Width.HasValue && primary.Height.HasValue)
                    {
                        var scale = w!.Value / (double)primary.Width.Value;
                        h = (int)Math.Round(primary.Height.Value * scale);
                    }
                    else if (givenH && !givenW && primary.Width.HasValue && primary.Height.HasValue)
                    {
                        var scale = h!.Value / (double)primary.Height.Value;
                        w = (int)Math.Round(primary.Width.Value * scale);
                    }
                    b.Resize(w, h, dpr);
                    if (primary.Name is { Length: > 0 }) b.Name(primary.Name);
                    if (primary.Primary) b.Primary();
                }
            }
        }

        // ------- Format / quality target the encode step -------
        var hasFormatOverride = p.ContainsKey("format") || p.ContainsKey("quality");
        if (hasFormatOverride)
        {
            var formatRaw = Take(p, "format");
            var qualityRaw = Take(p, "quality");
            if (formatRaw is not null && !allowed.HasFlag(MutatorKind.Format))
            {
                Reject(p, ignored, rejected, strict, "format", formatRaw);
                formatRaw = null;
            }
            if (qualityRaw is not null && !allowed.HasFlag(MutatorKind.Quality))
            {
                Reject(p, ignored, rejected, strict, "quality", qualityRaw);
                qualityRaw = null;
            }
            if (formatRaw is not null || qualityRaw is not null)
            {
                var encode = seed.Steps.OfType<EncodeStep>().FirstOrDefault();
                var existingFormat = encode?.Format;
                int q = encode?.Quality ?? Quality.Web;
                if (qualityRaw is not null) Quality.TryParse(qualityRaw, out q);
                var finalFormat = formatRaw?.ToLowerInvariant() ?? existingFormat;
                if (finalFormat is null) b.PreserveFormat(q);
                else b.EncodeAs(finalFormat, q);
            }
        }

        // ------- Frame override -------
        if (p.ContainsKey("frame"))
        {
            var v = Take(p, "frame")!;
            if (!allowed.HasFlag(MutatorKind.Frame))
            {
                Reject(p, ignored, rejected, strict, "frame", v);
            }
            else if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
            {
                b.ExtractFrame(idx);
            }
            else Reject(p, ignored, rejected, strict, "frame", v);
        }

        // ------- Crop / aspect / fit / position / bg target the shape step -------
        var cropRaw = Take(p, "crop");
        var fitRaw = Take(p, "fit");
        var positionRaw = Take(p, "position");
        var bgRaw = Take(p, "bg");
        if (cropRaw is not null && !allowed.HasFlag(MutatorKind.Crop))
        {
            Reject(p, ignored, rejected, strict, "crop", cropRaw);
            cropRaw = null;
        }
        if (fitRaw is not null && !allowed.HasFlag(MutatorKind.Fit))
        {
            Reject(p, ignored, rejected, strict, "fit", fitRaw);
            fitRaw = null;
        }
        if (positionRaw is not null && !allowed.HasFlag(MutatorKind.Position))
        {
            Reject(p, ignored, rejected, strict, "position", positionRaw);
            positionRaw = null;
        }
        if (bgRaw is not null && !allowed.HasFlag(MutatorKind.Background))
        {
            Reject(p, ignored, rejected, strict, "bg", bgRaw);
            bgRaw = null;
        }
        if (cropRaw is not null || fitRaw is not null || positionRaw is not null || bgRaw is not null)
        {
            var existing = seed.Steps.OfType<ShapeStep>().FirstOrDefault();
            CropSpec? finalCrop = existing?.Crop;
            if (cropRaw is not null && CropSpec.TryParse(cropRaw, out var cs)) finalCrop = cs;

            Fit finalFit = existing?.Fit ?? Fit.Cover;
            if (fitRaw is not null && TryParseFit(fitRaw, out var pf)) finalFit = pf;

            Position finalPos = existing?.Position ?? Position.Center;
            if (positionRaw is not null && Position.TryParse(positionRaw, out var pp)) finalPos = pp;

            Background finalBg = existing?.Background ?? Background.Transparent();
            if (bgRaw is not null && Background.TryParse(bgRaw, out var pb)) finalBg = pb;

            // Position without any crop (recipe or override) → reject per ADR
            if (finalCrop is null)
            {
                if (positionRaw is not null) Reject(p, ignored, rejected, strict, "position", positionRaw);
            }
            else
            {
                b.Shape(crop: finalCrop, fit: finalFit, position: finalPos, bg: finalBg);
            }
        }

        // ------- Rotate / flip -------
        if (p.ContainsKey("rotate"))
        {
            var v = Take(p, "rotate")!;
            if (!allowed.HasFlag(MutatorKind.Rotate)) Reject(p, ignored, rejected, strict, "rotate", v);
            else if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var deg))
                b.Rotate(deg);
            else Reject(p, ignored, rejected, strict, "rotate", v);
        }
        if (p.ContainsKey("flip"))
        {
            var v = Take(p, "flip")!;
            if (!allowed.HasFlag(MutatorKind.Rotate)) Reject(p, ignored, rejected, strict, "flip", v);
            else if (string.Equals(v, "v", StringComparison.OrdinalIgnoreCase)) b.FlipVertical();
            else if (string.Equals(v, "h", StringComparison.OrdinalIgnoreCase)) b.FlipHorizontal();
            else Reject(p, ignored, rejected, strict, "flip", v);
        }

        // ------- Strip -------
        if (p.ContainsKey("strip"))
        {
            var v = Take(p, "strip")!;
            if (!allowed.HasFlag(MutatorKind.Strip)) Reject(p, ignored, rejected, strict, "strip", v);
            else
            {
                var k = ParseStripKinds(v);
                if (k is null) Reject(p, ignored, rejected, strict, "strip", v);
                else b.Strip(k.Value);
            }
        }

        // Remaining keys are unrecognised
        foreach (var (k, v) in p) Reject(new Dictionary<string, string>(), ignored, rejected, strict, k, v);
    }

    private static void ApplyNamedStep(
        MediaRecipeBuilder b,
        MediaRecipe seed,
        string stepName,
        Dictionary<string, string> bag,
        MutatorKind allowed,
        List<string> ignored,
        List<string> rejected,
        bool strict)
    {
        var step = seed.Steps.FirstOrDefault(s => string.Equals(s.Name, stepName, StringComparison.OrdinalIgnoreCase));
        if (step is null)
        {
            foreach (var (k, v) in bag) Reject(bag, ignored, rejected, strict, $"{stepName}.{k}", v);
            return;
        }
        // For now, only support dimension overrides on ResizeStep — the most common case
        if (step is ResizeStep rz)
        {
            if (!allowed.HasFlag(MutatorKind.Dimensions))
            {
                foreach (var (k, v) in bag) Reject(bag, ignored, rejected, strict, $"{stepName}.{k}", v);
                return;
            }
            int? w = rz.Width, h = rz.Height;
            double dpr = rz.Dpr;
            if (bag.TryGetValue("width", out var wRaw) || bag.TryGetValue("w", out wRaw))
            {
                if (int.TryParse(wRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var wp)) w = wp;
            }
            if (bag.TryGetValue("height", out var hRaw) || bag.TryGetValue("h", out hRaw))
            {
                if (int.TryParse(hRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hp)) h = hp;
            }
            if (bag.TryGetValue("dpr", out var dRaw))
            {
                if (double.TryParse(dRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) dpr = d;
            }
            b.Resize(w, h, dpr);
            if (step.Name is { Length: > 0 }) b.Name(step.Name);
            if (step.Primary) b.Primary();
        }
        else
        {
            // Other named-step mutations land in follow-up work; reject for now
            foreach (var (k, v) in bag) Reject(bag, ignored, rejected, strict, $"{stepName}.{k}", v);
        }
    }

    /// <summary>
    /// Per MEDIA-0004 §7. Extracts overlay layer definitions from the URL
    /// param dict, removing them from <paramref name="all"/> so the regular
    /// step-name-prefix parser doesn't try to treat <c>overlay.0.position</c>
    /// as a step named "overlay".
    ///
    /// <para>Recognised forms:</para>
    /// <list type="bullet">
    ///   <item><c>?overlay=ID</c> → layer 0 with media source ID</item>
    ///   <item><c>?overlay.id=ID</c> → layer 0 (sugar)</item>
    ///   <item><c>?overlay.N.id=ID</c> / <c>overlay.N.field=val</c> → layer N</item>
    ///   <item><c>?overlay.text=...</c> / <c>overlay.N.text=...</c> → text source</item>
    /// </list>
    /// </summary>
    private static List<OverlayLayer> ExtractOverlayLayers(
        IDictionary<string, string> all,
        List<string> ignored,
        List<string> rejected,
        bool strict)
    {
        // Bucket params per layer index. Layer 0 catches the bare `overlay=`
        // and `overlay.<field>=` forms; `overlay.N.<field>=` selects by index.
        var bucketsByIndex = new SortedDictionary<int, Dictionary<string, string>>();
        var keysToRemove = new List<string>();

        foreach (var (key, value) in all)
        {
            if (!key.StartsWith("overlay", StringComparison.OrdinalIgnoreCase)) continue;

            // Bare ?overlay=ID → layer 0 'id'
            if (key.Equals("overlay", StringComparison.OrdinalIgnoreCase))
            {
                EnsureBucket(bucketsByIndex, 0)["id"] = value;
                keysToRemove.Add(key);
                continue;
            }

            // overlay.<rest>
            if (!key.StartsWith("overlay.", StringComparison.OrdinalIgnoreCase)) continue;

            var rest = key["overlay.".Length..];
            int idx = 0;
            string field = rest;
            // Try numeric prefix (overlay.N.field)
            var dot = rest.IndexOf('.', StringComparison.Ordinal);
            if (dot > 0 && int.TryParse(rest[..dot], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsedIdx))
            {
                idx = parsedIdx;
                field = rest[(dot + 1)..];
            }
            EnsureBucket(bucketsByIndex, idx)[field] = value;
            keysToRemove.Add(key);
        }

        foreach (var k in keysToRemove) all.Remove(k);

        var layers = new List<OverlayLayer>();
        foreach (var (idx, bag) in bucketsByIndex)
        {
            if (TryBuildLayerFromBag(bag, idx, ignored, rejected, strict, out var layer))
            {
                layers.Add(layer);
            }
        }
        return layers;
    }

    private static Dictionary<string, string> EnsureBucket(SortedDictionary<int, Dictionary<string, string>> dict, int idx)
    {
        if (!dict.TryGetValue(idx, out var bag))
        {
            bag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            dict[idx] = bag;
        }
        return bag;
    }

    private static bool TryBuildLayerFromBag(
        Dictionary<string, string> bag,
        int layerIndex,
        List<string> ignored,
        List<string> rejected,
        bool strict,
        out OverlayLayer layer)
    {
        layer = null!;

        OverlaySource? source = null;
        if (bag.TryGetValue("text", out var textVal))
        {
            BackgroundColor? color = null;
            if (bag.TryGetValue("color", out var colorRaw))
            {
                if (!BackgroundColor.TryParse(colorRaw, out var parsedColor))
                {
                    Reject(bag, ignored, rejected, strict, $"overlay.{layerIndex}.color", colorRaw);
                }
                else color = parsedColor;
            }
            int fontSize = 32;
            if (bag.TryGetValue("fontsize", out var sizeRaw))
            {
                if (!int.TryParse(sizeRaw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out fontSize) || fontSize <= 0)
                {
                    Reject(bag, ignored, rejected, strict, $"overlay.{layerIndex}.fontSize", sizeRaw);
                    fontSize = 32;
                }
            }
            source = new TextOverlaySource(textVal, bag.GetValueOrDefault("font"), color, fontSize);
        }
        else if (bag.TryGetValue("id", out var idVal))
        {
            source = new MediaOverlaySource(idVal, bag.GetValueOrDefault("recipe"));
        }
        else
        {
            // No id and no text — skip the layer
            return false;
        }

        OverlaySize size = OverlaySize.Natural;
        if (bag.TryGetValue("size", out var sizeRaw2))
        {
            if (!OverlaySize.TryParse(sizeRaw2, out size))
            {
                Reject(bag, ignored, rejected, strict, $"overlay.{layerIndex}.size", sizeRaw2);
                size = OverlaySize.Natural;
            }
        }

        Position position = Position.Center;
        if (bag.TryGetValue("position", out var posRaw))
        {
            if (!Position.TryParse(posRaw, out position))
            {
                Reject(bag, ignored, rejected, strict, $"overlay.{layerIndex}.position", posRaw);
                position = Position.Center;
            }
        }

        OverlayPadding padding = OverlayPadding.Zero;
        if (bag.TryGetValue("padding", out var padRaw))
        {
            if (!OverlayPadding.TryParse(padRaw, out padding))
            {
                Reject(bag, ignored, rejected, strict, $"overlay.{layerIndex}.padding", padRaw);
                padding = OverlayPadding.Zero;
            }
        }

        double opacity = 1.0;
        if (bag.TryGetValue("opacity", out var opaRaw))
        {
            if (!double.TryParse(opaRaw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out opacity))
            {
                Reject(bag, ignored, rejected, strict, $"overlay.{layerIndex}.opacity", opaRaw);
                opacity = 1.0;
            }
        }

        int rotate = 0;
        if (bag.TryGetValue("rotate", out var rotRaw))
        {
            if (!int.TryParse(rotRaw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out rotate))
            {
                Reject(bag, ignored, rejected, strict, $"overlay.{layerIndex}.rotate", rotRaw);
                rotate = 0;
            }
        }

        layer = new OverlayLayer(source, size, position, padding, Math.Clamp(opacity, 0.0, 1.0), rotate);
        return true;
    }

    private static IEnumerable<(string Key, string Value)> EnumerateRejectedOverlayParams(IList<OverlayLayer> layers)
    {
        for (var i = 0; i < layers.Count; i++)
        {
            yield return ($"overlay.{i}", layers[i].Source switch
            {
                MediaOverlaySource m => m.MediaId,
                TextOverlaySource t => $"text:{t.Text}",
                _ => "",
            });
        }
    }

    private static void ApplyOverlayLayers(MediaRecipeBuilder builder, IList<OverlayLayer> layers, bool mutatorAllowed)
    {
        if (layers.Count == 0) return;
        if (!mutatorAllowed) return;
        foreach (var layer in layers)
        {
            builder.Overlay(layer);
        }
    }

    private static string? Take(IDictionary<string, string> p, string key)
    {
        if (!p.TryGetValue(key, out var v)) return null;
        p.Remove(key);
        return v;
    }

    private static int? TakeInt(IDictionary<string, string> p, string key,
        List<string> ignored, List<string> rejected, bool strict)
    {
        var raw = Take(p, key);
        if (raw is null) return null;
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0)
            return v;
        Reject(p, ignored, rejected, strict, key, raw);
        return null;
    }

    private static void Reject(
        IDictionary<string, string> _,
        List<string> ignored,
        List<string> rejected,
        bool strict,
        string key,
        string value)
    {
        var entry = $"{key}={value}";
        if (strict) rejected.Add(entry);
        else ignored.Add(entry);
    }

    private static bool TryParseFit(string value, out Fit fit)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "cover": fit = Fit.Cover; return true;
            case "contain": fit = Fit.Contain; return true;
            case "fill": fit = Fit.Fill; return true;
            case "scale-down" or "scaledown": fit = Fit.ScaleDown; return true;
            case "none": fit = Fit.None; return true;
            default: fit = Fit.Cover; return false;
        }
    }

    private static MetadataKinds? ParseStripKinds(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return MetadataKinds.All;
        var kinds = MetadataKinds.None;
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "exif": kinds |= MetadataKinds.Exif; break;
                case "icc": kinds |= MetadataKinds.Icc; break;
                case "xmp": kinds |= MetadataKinds.Xmp; break;
                case "all": kinds |= MetadataKinds.All; break;
                default: return null;
            }
        }
        return kinds;
    }
}
