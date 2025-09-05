using System;
using System.Text.RegularExpressions;

namespace Sora.Flow.Infrastructure;

public static class FlowSets
{
    // Stage names
    public const string Intake = "intake";
    public const string Standardized = "standardized";
    public const string Keyed = "keyed";
    public const string Parked = "parked";
    public const string Processed = "processed";
    public const string Tasks = "tasks";

    public static string ModelName<TModel>() => ModelName(typeof(TModel));
    public static string ModelName(Type t)
    {
        var name = t.Name;
        // Simple kebab-case-ish normalization: PascalCase -> pascal-case; trim common suffixes
        name = Regex.Replace(name, "([a-z0-9])([A-Z])", "$1-$2").ToLowerInvariant();
        name = name.Replace("-model", string.Empty);
        return name;
    }

    // Full set names (include model segment) — still useful for some providers/scopes
    public static string Stage<TModel>(string stage) => $"flow.{ModelName<TModel>()}.{stage}";
    public static string Stage(Type model, string stage) => $"flow.{ModelName(model)}.{stage}";
    public static string View<TModel>(string view) => $"flow.{ModelName<TModel>()}.views.{view}";
    public static string View(Type model, string view) => $"flow.{ModelName(model)}.views.{view}";
    public static string TaskSet<TModel>() => $"flow.{ModelName<TModel>()}.{Tasks}";
    public static string TaskSet(Type model) => $"flow.{ModelName(model)}.{Tasks}";

    // Short set names (no model segment) — used with storage base overridden to model full name
    public static string StageShort(string stage) => $"flow.{stage}";
    public static string ViewShort(string view) => $"flow.views.{view}";
    public static string TasksShort() => $"flow.{Tasks}";
}
