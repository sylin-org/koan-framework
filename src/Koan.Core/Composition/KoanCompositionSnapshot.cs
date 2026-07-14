using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using Microsoft.Extensions.Configuration;
using Koan.Core.Hosting.Registry;
using Koan.Core.Diagnostics;
using Koan.Core.Infrastructure;

namespace Koan.Core.Composition;

/// <summary>
/// Builds the boot-time resolved composition twin from a live host: the kernel-knowable sections
/// (app, modules, Koan config keys) plus whatever <see cref="IKoanCompositionContributor"/>s enrich
/// (elections, capabilities, entities). Module versions are normalized to major.minor to match the
/// build-time <c>koan.lock.json</c> so the two compare cleanly.
/// </summary>
internal static class KoanCompositionSnapshot
{
    /// <summary>The resolved-twin path relative to the content root (gitignored build artifact).</summary>
    public const string ResolvedTwinRelativePath = "obj/koan.lock.resolved.json";

    public static KoanLockfile Build(IServiceProvider services, string appName, IConfiguration? config)
        => BuildResult(services, appName, config).Lockfile;

    internal static KoanCompositionResult BuildResult(IServiceProvider services, string appName, IConfiguration? config)
    {
        // "Modules" = the Koan.* assemblies the app is composed of — the SAME notion the build-time
        // emitter records from MSBuild references. The bootstrap loads the full reference closure for
        // scanning, so at boot the loaded set matches the referenced set and the two files compare cleanly.
        var lockModules = GetLoadedKoanModules();
        var koan = lockModules.FirstOrDefault(m => string.Equals(m.Id, "Koan.Core", StringComparison.Ordinal))?.Version ?? "unknown";
        var app = new KoanLockApp(string.IsNullOrWhiteSpace(appName) ? "app" : appName, koan, ResolveTfm());

        var builder = new KoanCompositionBuilder();
        if (config is not null) AddConfigKeys(builder, config);
        RunContributors(builder, services);

        builder.ApplyTo(out var elections, out var capabilities, out var configKeys, out var entities, out var facts);
        var lockfile = new KoanLockfile(KoanLockfile.CurrentSchema, app, lockModules, elections, capabilities, configKeys, entities);
        return new KoanCompositionResult(lockfile, facts);
    }

    /// <summary>Build the resolved twin from a live host (app name from <see cref="KoanEnv"/>).</summary>
    public static KoanLockfile BuildFromServices(IServiceProvider services)
    {
        var appName = KoanEnv.CurrentSnapshot.Application.Name;
        var config = services.GetService(typeof(IConfiguration)) as IConfiguration;
        return Build(services, appName, config);
    }

    private static IReadOnlyList<KoanLockModule> GetLoadedKoanModules()
        => AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .Select(a => a.GetName())
            .Where(n => !string.IsNullOrEmpty(n.Name)
                && (n.Name!.StartsWith("Koan.", StringComparison.Ordinal) || n.Name == "Koan")
                && !n.Name.EndsWith(".Generators", StringComparison.Ordinal)
                && !n.Name.StartsWith("Koan.Testing", StringComparison.Ordinal))
            .Select(n => new KoanLockModule(n.Name!, MajorMinor(n.Version?.ToString())))
            .GroupBy(m => m.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(m => m.Id, StringComparer.Ordinal)
            .ToArray();

    /// <summary>Write the resolved twin to <c>{contentRoot}/obj/koan.lock.resolved.json</c>. Best-effort.</summary>
    public static void TryWriteResolvedTwin(KoanLockfile lockfile, string? contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(contentRootPath)) return;
        try
        {
            var path = Path.Combine(contentRootPath, "obj", "koan.lock.resolved.json");
            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return; // never create obj/ ourselves
            File.WriteAllText(path, KoanLockfileSerializer.Serialize(lockfile));
        }
        catch
        {
            // A diagnostic artifact: a read-only or absent obj/ must never disrupt boot.
        }
    }

    private static void AddConfigKeys(KoanCompositionBuilder builder, IConfiguration config)
    {
        foreach (var kv in config.AsEnumerable())
        {
            // KEYS only, never values; leaf keys (those with a value) under the Koan: namespace.
            if (kv.Key is null || kv.Value is null) continue;
            if (kv.Key.StartsWith("Koan:", StringComparison.OrdinalIgnoreCase))
                builder.AddConfigKey(kv.Key);
        }
    }

    private static void RunContributors(KoanCompositionBuilder builder, IServiceProvider services)
    {
        Type[] contributors;
        try { contributors = KoanRegistry.GetDiscoveredImplementors(typeof(IKoanCompositionContributor)); }
        catch { return; }

        foreach (var type in contributors)
        {
            try
            {
                if (type.IsAbstract) continue;
                if (Activator.CreateInstance(type) is IKoanCompositionContributor contributor)
                    contributor.Contribute(builder, services);
            }
            catch (Exception ex)
            {
                builder.AddFact(KoanFact.Create(
                    Constants.Diagnostics.Codes.CollectionFailed,
                    KoanFactKind.Degradation,
                    KoanFactState.CollectionFailed,
                    type.FullName ?? type.Name,
                    "A composition contributor could not report its runtime facts.",
                    Constants.Diagnostics.Reasons.ReporterFailed,
                    "Inspect the named contributor and retry startup after correcting its reporting failure.",
                    type.Assembly.GetName().Name ?? "composition",
                    $"composition:{type.FullName ?? type.Name}:{ex.GetType().Name}"));
            }
        }
    }

    /// <summary>Normalize any version string to major.minor (breaking tier); pass non-versions through.</summary>
    internal static string MajorMinor(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return "unknown";
        var core = version.Split('-', '+')[0];
        var parts = core.Split('.');
        if (parts.Length >= 2 && int.TryParse(parts[0], out _) && int.TryParse(parts[1], out _))
            return parts[0] + "." + parts[1];
        return version;
    }

    private static string ResolveTfm()
    {
        var fwk = Assembly.GetEntryAssembly()?.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
        const string marker = "Version=v";
        if (!string.IsNullOrEmpty(fwk))
        {
            var idx = fwk.IndexOf(marker, StringComparison.Ordinal);
            if (idx >= 0) return "net" + fwk.Substring(idx + marker.Length);
        }
        var v = Environment.Version;
        return $"net{v.Major}.{v.Minor}";
    }
}

internal sealed record KoanCompositionResult(
    KoanLockfile Lockfile,
    IReadOnlyList<KoanFact> Facts);
