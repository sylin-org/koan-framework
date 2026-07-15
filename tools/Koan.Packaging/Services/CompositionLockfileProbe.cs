using System.Text.Json;
using Koan.Packaging.Infrastructure;

namespace Koan.Packaging.Services;

internal static class CompositionLockfileProbe
{
    public static bool Require(
        string applicationDirectory,
        string expectedApplicationName,
        params string[] requiredModules)
    {
        var path = Path.Combine(
            applicationDirectory,
            PackagingConstants.ApplicationProbe.CompositionLockfileName);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"The application build did not emit {PackagingConstants.ApplicationProbe.CompositionLockfileName}: {path}");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.GetProperty(PackagingConstants.ApplicationProbe.CompositionSchemaProperty).GetInt32()
            != PackagingConstants.ApplicationProbe.CompositionLockfileSchema)
            throw new InvalidOperationException("The composition lockfile uses an unsupported schema.");

        var app = root.GetProperty(PackagingConstants.ApplicationProbe.CompositionAppProperty);
        var actualApplicationName = app.GetProperty(PackagingConstants.ApplicationProbe.CompositionNameProperty).GetString();
        var koanVersion = app.GetProperty(PackagingConstants.ApplicationProbe.CompositionVersionProperty).GetString();
        var targetFramework = app.GetProperty(PackagingConstants.ApplicationProbe.CompositionTargetFrameworkProperty).GetString();
        if (!string.Equals(actualApplicationName, expectedApplicationName, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(koanVersion)
            || string.Equals(koanVersion, PackagingConstants.ApplicationProbe.UnknownCompositionVersion, StringComparison.Ordinal)
            || !string.Equals(targetFramework, PackagingConstants.ApplicationProbe.TargetFramework, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The composition lockfile identity is invalid: app={actualApplicationName}, Koan={koanVersion}, tfm={targetFramework}.");
        }

        var modules = root.GetProperty(PackagingConstants.ApplicationProbe.CompositionModulesProperty)
            .EnumerateArray()
            .Select(module => module.GetProperty(PackagingConstants.ApplicationProbe.CompositionModuleIdProperty).GetString())
            .Where(module => !string.IsNullOrWhiteSpace(module))
            .ToHashSet(StringComparer.Ordinal);
        var missing = requiredModules.Where(module => !modules.Contains(module)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"The composition lockfile is missing required modules: {string.Join(", ", missing)}.");
        }

        return true;
    }

    public static bool RequireRuntimeMatch(JsonElement runtimeFacts)
    {
        var facts = runtimeFacts
            .GetProperty(PackagingConstants.ApplicationProbe.FactsProperty)
            .EnumerateArray()
            .ToArray();
        var drift = facts.FirstOrDefault(fact =>
            fact.GetProperty(PackagingConstants.ApplicationProbe.FactCodeProperty).GetString()
                == PackagingConstants.ApplicationProbe.LockfileDriftedCode);
        if (drift.ValueKind != JsonValueKind.Undefined)
        {
            var summary = drift.TryGetProperty(
                PackagingConstants.ApplicationProbe.FactSummaryProperty,
                out var summaryProperty)
                ? summaryProperty.GetString()
                : null;
            throw new InvalidOperationException(
                $"The running application reports composition lockfile drift: {summary ?? "no summary"}.");
        }

        var matched = facts.Any(fact =>
            fact.GetProperty(PackagingConstants.ApplicationProbe.FactCodeProperty).GetString()
                == PackagingConstants.ApplicationProbe.LockfileMatchedCode);
        if (!matched)
            throw new InvalidOperationException("The running application did not report a matched composition lockfile.");

        return true;
    }
}
