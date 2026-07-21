using System.Text.Json;
using System.Text.Json.Nodes;
using Koan.Media.Abstractions.Recipes;

namespace Koan.Media.Core.Recipes;

/// <summary>
/// Renders a <see cref="MediaRecipe"/> as the canonical JSON shape that
/// <c>/media/recipes/{name}</c> emits and <see cref="ConfiguredRecipeBinder"/>
/// accepts. Round-trippable: the JSON written here can be pasted into
/// <c>Koan:Media:Recipes</c> and produces the same recipe. Per MEDIA-0004 §9.
///
/// <para>The <c>as=appsettings</c> query wraps the result under
/// <c>Koan:Media:Recipes:{slug}</c> for direct copy-paste into a
/// settings file.</para>
/// </summary>
public static class RecipeJsonSerializer
{
    public static JsonObject Serialize(MediaRecipe recipe)
    {
        var obj = new JsonObject
        {
            ["name"] = recipe.Name,
            ["version"] = recipe.Version,
            ["description"] = recipe.Description,
            ["source"] = recipe.Source switch
            {
                RecipeSource.Code => "code",
                RecipeSource.Config => "config",
                RecipeSource.ConfigOverride => "config-override",
                RecipeSource.AdHoc => "ad-hoc",
                _ => "code",
            },
            ["fingerprint"] = recipe.Fingerprint(),
            ["steps"] = SerializeSteps(recipe.Steps),
            ["mutators"] = SerializeMutators(recipe.AllowedMutators),
        };
        return obj;
    }

    public static JsonObject SerializeAll(IReadOnlyList<MediaRecipe> recipes, IReadOnlyList<string> formatShortcuts)
    {
        var arr = new JsonArray();
        foreach (var r in recipes) arr.Add(Serialize(r));

        return new JsonObject
        {
            ["recipes"] = arr,
            ["formatShortcuts"] = new JsonArray(formatShortcuts.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray()),
            ["adHocSteps"] = new JsonArray(
                new JsonNode[] { "fit", "cover", "crop", "rotate", "flip", "strip", "frame", "format", "quality", "position", "bg" }),
            ["paramAliases"] = new JsonObject
            {
                ["w"] = "width",
                ["h"] = "height",
                ["q"] = "quality",
                ["f"] = "format",
            },
        };
    }

    /// <summary>
    /// Wrap a recipe under <c>Koan:Media:Recipes:{name}</c> so the
    /// result is ready to drop into <c>appsettings.json</c>.
    /// </summary>
    public static JsonObject SerializeAsAppSettings(MediaRecipe recipe)
    {
        var configured = StripIntrospectionFields(Serialize(recipe));
        return new JsonObject
        {
            ["Koan"] = new JsonObject
            {
                ["Media"] = new JsonObject
                {
                    ["Recipes"] = new JsonObject
                    {
                        [recipe.Name ?? "unnamed"] = configured,
                    },
                },
            },
        };
    }

    private static JsonNode StripIntrospectionFields(JsonObject src)
    {
        // For round-trip, remove fields the binder doesn't read.
        var copy = new JsonObject();
        foreach (var (k, v) in src)
        {
            if (k is "name" or "fingerprint" or "source") continue;
            copy[k] = v?.DeepClone();
        }
        return copy;
    }

    private static JsonArray SerializeSteps(IReadOnlyList<MediaStep> steps)
    {
        var arr = new JsonArray();
        foreach (var step in steps.OrderBy(s => (int)s.Stage))
        {
            var obj = new JsonObject();
            switch (step)
            {
                case AutoOrientStep ao:
                    obj["op"] = "autoOrient";
                    if (ao.Keep) obj["keep"] = true;
                    break;
                case SampleStep ss:
                    obj["op"] = "sample";
                    switch (ss.Selector)
                    {
                        case FrameSelector.Index idx:
                            obj["selector"] = "index";
                            obj["index"] = idx.Frame;
                            break;
                        case FrameSelector.Time t:
                            obj["selector"] = "time";
                            obj["timeMs"] = t.At.TotalMilliseconds;
                            break;
                        case FrameSelector.HeuristicBest:
                            obj["selector"] = "thumbnail";
                            break;
                    }
                    break;
#pragma warning disable CS0618 // Retained for any direct ExtractFrameStep construction outside the builders.
                case ExtractFrameStep ef:
                    obj["op"] = "extractFrame";
                    obj["index"] = ef.Index;
                    break;
#pragma warning restore CS0618
                case RotateStep rs:
                    obj["op"] = "rotate";
                    obj["degrees"] = rs.Degrees;
                    break;
                case FlipStep fs:
                    obj["op"] = "flip";
                    obj["axis"] = fs.Axis == FlipAxis.Horizontal ? "h" : "v";
                    break;
                case ShapeStep ss:
                    obj["op"] = "shape";
                    if (ss.Crop is { } c) obj["crop"] = c.ToCanonical();
                    obj["mode"] = ss.Fit.ToString().ToLowerInvariant();
                    obj["position"] = ss.Position.ToCanonical();
                    obj["bg"] = ss.Background.ToCanonical();
                    break;
                case ResizeStep rz:
                    obj["op"] = "resize";
                    if (rz.Width.HasValue) obj["width"] = rz.Width.Value;
                    if (rz.Height.HasValue) obj["height"] = rz.Height.Value;
                    if (Math.Abs(rz.Dpr - 1.0) > 0.001) obj["dpr"] = rz.Dpr;
                    break;
                case OverlayStep os:
                    obj["op"] = "overlay";
                    obj["layers"] = SerializeOverlayLayers(os.Layers);
                    break;
                case StripStep st:
                    obj["op"] = "strip";
                    obj["kinds"] = SerializeStripKinds(st.Kinds);
                    break;
                case EncodeStep es:
                    obj["op"] = "encodeAs";
                    if (es.Format is not null) obj["format"] = es.Format;
                    obj["quality"] = es.Quality;
                    break;
                case FlattenToStep ft:
                    obj["op"] = "flattenTo";
                    obj["format"] = ft.Format;
                    obj["quality"] = ft.Quality;
                    break;
                default:
                    obj["op"] = step.GetType().Name;
                    break;
            }
            if (step.Name is { Length: > 0 } n) obj["name"] = n;
            if (step.Primary) obj["primary"] = true;
            arr.Add(obj);
        }
        return arr;
    }

    private static JsonArray SerializeOverlayLayers(System.Collections.Immutable.ImmutableArray<OverlayLayer> layers)
    {
        var arr = new JsonArray();
        foreach (var layer in layers)
        {
            var obj = new JsonObject
            {
                ["source"] = SerializeOverlaySource(layer.Source),
                ["size"] = layer.Size.ToCanonical(),
                ["position"] = layer.Position.ToCanonical(),
                ["padding"] = layer.Padding.ToCanonical(),
                ["opacity"] = layer.Opacity,
            };
            if (layer.Rotate != 0) obj["rotate"] = layer.Rotate;
            arr.Add(obj);
        }
        return arr;
    }

    private static JsonObject SerializeOverlaySource(OverlaySource source) => source switch
    {
        MediaOverlaySource m => new JsonObject
        {
            ["kind"] = "media",
            ["mediaId"] = m.MediaId,
            ["recipe"] = m.RecipeName,
        },
        TextOverlaySource t => new JsonObject
        {
            ["kind"] = "text",
            ["text"] = t.Text,
            ["font"] = t.Font,
            ["color"] = t.Color?.ToCanonical(),
            ["fontSize"] = t.FontSize,
        },
        _ => new JsonObject { ["kind"] = "unknown" },
    };

    private static string SerializeStripKinds(MetadataKinds kinds)
    {
        if (kinds == MetadataKinds.All) return "all";
        var parts = new List<string>(3);
        if (kinds.HasFlag(MetadataKinds.Exif)) parts.Add("exif");
        if (kinds.HasFlag(MetadataKinds.Icc)) parts.Add("icc");
        if (kinds.HasFlag(MetadataKinds.Xmp)) parts.Add("xmp");
        return string.Join(",", parts);
    }

    private static JsonArray SerializeMutators(MutatorKind kinds)
    {
        var arr = new JsonArray();
        foreach (MutatorKind kind in Enum.GetValues<MutatorKind>())
        {
            if (kind == MutatorKind.None) continue;
            if (kind == MutatorKind.All || kind == MutatorKind.Common) continue;
            if ((kinds & kind) != kind) continue;
            arr.Add(kind.ToString().ToLowerInvariant());
        }
        return arr;
    }

    private static readonly JsonSerializerOptions s_indented = new()
    {
        WriteIndented = true,
    };

    public static string ToIndentedString(JsonObject obj) => obj.ToJsonString(s_indented);
}
