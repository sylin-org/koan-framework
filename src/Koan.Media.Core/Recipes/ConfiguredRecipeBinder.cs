using Koan.Media.Abstractions.Recipes;

namespace Koan.Media.Core.Recipes;

/// <summary>
/// Translates a <see cref="ConfiguredRecipe"/> (JSON / appsettings
/// shape) into an immutable <see cref="MediaRecipe"/>. Per
/// MEDIA-0004 §3, this is the boot-time validator: unknown ops,
/// unknown mutators, and invalid step grammar fail-fast with the
/// offending path in the message.
/// </summary>
public static class ConfiguredRecipeBinder
{
    public static MediaRecipe Bind(string name, ConfiguredRecipe configured, RecipeSource source)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new MediaRecipeBindingException("Recipe name is required.");

        var builder = MediaRecipe.New().WithName(name).WithVersion(configured.Version);
        if (configured.Description is { Length: > 0 } d) builder.WithDescription(d);
        if (configured.Eager) builder.WithEager();

        var mutators = MutatorKind.None;
        foreach (var m in configured.Mutators ?? new())
        {
            if (!TryParseMutator(m, out var kind))
                throw new MediaRecipeBindingException(
                    $"Recipe '{name}': unknown mutator '{m}'. Allowed: {string.Join(", ", AllMutatorNames)}.");
            mutators |= kind;
        }
        builder.Mutators(mutators);

        for (var i = 0; i < configured.Steps.Count; i++)
        {
            var cs = configured.Steps[i];
            try
            {
                ApplyStep(builder, cs);
                // Apply name/primary if set on this step
                if (cs.Name is { Length: > 0 }) builder.Name(cs.Name);
                if (cs.Primary) builder.Primary();
            }
            catch (MediaRecipeBindingException) { throw; }
            catch (Exception ex)
            {
                throw new MediaRecipeBindingException(
                    $"Recipe '{name}' step[{i}] (op='{cs.Op}'): {ex.Message}", ex);
            }
        }

        var recipe = builder.Build() with { Source = source };
        return recipe;
    }

    private static void ApplyStep(MediaRecipeBuilder builder, ConfiguredStep s)
    {
        switch (s.Op?.Trim().ToLowerInvariant())
        {
            case "autoorient" or "orient":
                builder.AutoOrient(keep: s.Keep);
                break;
            case "extractframe" or "frame" or "sample":
                // MEDIA-0005: extractframe/frame are legacy slugs for Sample.
                // All three route to the canonical Sample step with an Index
                // selector; richer selectors (time, thumbnail) are
                // configuration-deferred until the SVG/Timeline decoders
                // land.
                builder.Sample(new FrameSelector.Index(s.Index));
                break;
            case "rotate":
                builder.Rotate(s.Degrees);
                break;
            case "flip":
                if (string.Equals(s.Axis, "v", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(s.Axis, "vertical", StringComparison.OrdinalIgnoreCase))
                    builder.FlipVertical();
                else
                    builder.FlipHorizontal();
                break;
            case "crop":
            {
                var cropRaw = s.Crop ?? s.Aspect
                    ?? throw new MediaRecipeBindingException("'crop' step requires 'crop' or 'aspect'.");
                if (!CropSpec.TryParse(cropRaw, out var spec))
                    throw new MediaRecipeBindingException($"Invalid crop value '{cropRaw}'.");
                builder.Crop(spec);
                break;
            }
            case "shape" or "fit":
            {
                CropSpec? crop = null;
                if (s.Crop is not null || s.Aspect is not null)
                {
                    var rawCrop = s.Crop ?? s.Aspect!;
                    if (!CropSpec.TryParse(rawCrop, out var c))
                        throw new MediaRecipeBindingException($"Invalid crop value '{rawCrop}'.");
                    crop = c;
                }
                var fit = ParseFit(s.Mode);
                Position? pos = null;
                if (s.Position is not null)
                {
                    if (!Position.TryParse(s.Position, out var p))
                        throw new MediaRecipeBindingException($"Invalid position '{s.Position}'.");
                    pos = p;
                }
                Background? bg = null;
                if (s.Bg is not null)
                {
                    if (!Background.TryParse(s.Bg, out var b))
                        throw new MediaRecipeBindingException($"Invalid bg value '{s.Bg}'.");
                    bg = b;
                }
                builder.Shape(crop: crop, fit: fit, position: pos, bg: bg);
                break;
            }
            case "resize":
                if (s.Width is null && s.Height is null)
                    throw new MediaRecipeBindingException("'resize' step requires at least one of width/height.");
                builder.Resize(s.Width, s.Height, s.Dpr);
                break;
            case "overlay":
                if (s.Layers is null || s.Layers.Count == 0)
                    throw new MediaRecipeBindingException("'overlay' step requires at least one layer.");
                foreach (var layer in s.Layers)
                {
                    builder.Overlay(BindOverlayLayer(layer));
                }
                break;
            case "strip":
                builder.Strip(ParseStripKinds(s.Kinds));
                break;
            case "encodeas" or "encode":
                if (s.Format is null) builder.PreserveFormat(s.Quality);
                else builder.EncodeAs(s.Format, s.Quality);
                break;
            case "flattento" or "flatten":
                if (s.Format is null)
                    throw new MediaRecipeBindingException("'flattenTo' step requires 'format'.");
                builder.FlattenTo(s.Format, s.Quality);
                break;
            default:
                throw new MediaRecipeBindingException(
                    $"Unknown op '{s.Op}'. Allowed: autoOrient, extractFrame, rotate, flip, crop, shape, fit, resize, strip, encodeAs, flattenTo.");
        }
    }

    private static OverlayLayer BindOverlayLayer(ConfiguredOverlayLayer layer)
    {
        var source = BindOverlaySource(layer.Source);

        OverlaySize size = OverlaySize.Natural;
        if (layer.Size is not null && !OverlaySize.TryParse(layer.Size, out size))
            throw new MediaRecipeBindingException($"Invalid overlay size '{layer.Size}'.");

        Position position = Position.Center;
        if (layer.Position is not null && !Position.TryParse(layer.Position, out position))
            throw new MediaRecipeBindingException($"Invalid overlay position '{layer.Position}'.");

        OverlayPadding padding = OverlayPadding.Zero;
        if (layer.Padding is not null && !OverlayPadding.TryParse(layer.Padding, out padding))
            throw new MediaRecipeBindingException($"Invalid overlay padding '{layer.Padding}'.");

        return new OverlayLayer(
            Source: source,
            Size: size,
            Position: position,
            Padding: padding,
            Opacity: Math.Clamp(layer.Opacity, 0.0, 1.0),
            Rotate: layer.Rotate);
    }

    private static OverlaySource BindOverlaySource(ConfiguredOverlaySource s)
    {
        var kind = (s.Kind ?? "media").Trim().ToLowerInvariant();
        switch (kind)
        {
            case "media":
                if (string.IsNullOrWhiteSpace(s.MediaId))
                    throw new MediaRecipeBindingException("Overlay source kind='media' requires 'mediaId'.");
                return new MediaOverlaySource(s.MediaId!, s.Recipe);
            case "text":
                if (string.IsNullOrWhiteSpace(s.Text))
                    throw new MediaRecipeBindingException("Overlay source kind='text' requires 'text'.");
                BackgroundColor? color = null;
                if (s.Color is not null)
                {
                    if (!BackgroundColor.TryParse(s.Color, out var parsed))
                        throw new MediaRecipeBindingException($"Invalid overlay text color '{s.Color}'.");
                    color = parsed;
                }
                return new TextOverlaySource(s.Text!, s.Font, color, s.FontSize);
            default:
                throw new MediaRecipeBindingException(
                    $"Unknown overlay source kind '{s.Kind}'. Allowed: media, text.");
        }
    }

    private static Fit ParseFit(string? mode) => (mode?.Trim().ToLowerInvariant()) switch
    {
        null or "" or "cover" => Fit.Cover,
        "contain" => Fit.Contain,
        "fill" => Fit.Fill,
        "scale-down" or "scaledown" => Fit.ScaleDown,
        "none" => Fit.None,
        _ => throw new MediaRecipeBindingException($"Invalid fit mode '{mode}'. Allowed: cover, contain, fill, scale-down, none."),
    };

    private static MetadataKinds ParseStripKinds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return MetadataKinds.All;
        var kinds = MetadataKinds.None;
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            kinds |= part.ToLowerInvariant() switch
            {
                "exif" => MetadataKinds.Exif,
                "icc" => MetadataKinds.Icc,
                "xmp" => MetadataKinds.Xmp,
                "all" => MetadataKinds.All,
                _ => throw new MediaRecipeBindingException($"Unknown strip kind '{part}'. Allowed: exif, icc, xmp, all."),
            };
        }
        return kinds;
    }

    private static readonly string[] AllMutatorNames =
    {
        "dimensions", "format", "quality", "frame", "position",
        "background", "crop", "fit", "overlay", "rotate", "strip",
        "common", "all",
    };

    private static bool TryParseMutator(string raw, out MutatorKind kind)
    {
        switch (raw.Trim().ToLowerInvariant())
        {
            case "dimensions": kind = MutatorKind.Dimensions; return true;
            case "format": kind = MutatorKind.Format; return true;
            case "quality": kind = MutatorKind.Quality; return true;
            case "frame": kind = MutatorKind.Frame; return true;
            case "position": kind = MutatorKind.Position; return true;
            case "background" or "bg": kind = MutatorKind.Background; return true;
            case "crop": kind = MutatorKind.Crop; return true;
            case "fit": kind = MutatorKind.Fit; return true;
            case "overlay": kind = MutatorKind.Overlay; return true;
            case "rotate": kind = MutatorKind.Rotate; return true;
            case "strip": kind = MutatorKind.Strip; return true;
            case "common": kind = MutatorKind.Common; return true;
            case "all": kind = MutatorKind.All; return true;
            default: kind = MutatorKind.None; return false;
        }
    }
}

/// <summary>
/// Boot-time binding error. Thrown when an appsettings recipe (or a
/// code-attribute recipe with invalid args) cannot be translated to a
/// valid <see cref="MediaRecipe"/>. The application should fail fast
/// — see MEDIA-0004 §3.
/// </summary>
public sealed class MediaRecipeBindingException : Exception
{
    public MediaRecipeBindingException(string message) : base(message) { }
    public MediaRecipeBindingException(string message, Exception inner) : base(message, inner) { }
}
