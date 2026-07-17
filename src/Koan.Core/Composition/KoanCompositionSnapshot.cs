using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Koan.Core.Hosting.Registry;
using Koan.Core.Diagnostics;
using Koan.Core.Infrastructure;
using Koan.Core.Semantics;
using Koan.Core.Semantics.Segmentation;

namespace Koan.Core.Composition;

/// <summary>
/// Builds the boot-time resolved composition twin from a live host: the kernel-knowable sections
/// (app, modules, Koan config keys) plus evidence projected by Core and active retained modules
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
        // "App" is the executable assembly identity and "Modules" are its Koan.* references — the SAME
        // separation the build-time emitter records from MSBuild. Friendly product identity belongs in
        // runtime facts, not in this build/runtime comparison contract.
        var applicationAssembly = ResolveApplicationAssembly(services);
        var lockModules = GetLoadedKoanModules(applicationAssembly);
        var koan = lockModules.FirstOrDefault(m => string.Equals(m.Id, "Koan.Core", StringComparison.Ordinal))?.Version ?? "unknown";
        var app = new KoanLockApp(ResolveApplicationName(applicationAssembly, appName), koan, ResolveTfm());

        var builder = new KoanCompositionBuilder();
        if (config is not null) AddConfigKeys(builder, config);
        SegmentationCompositionFacts.Project(builder, services);
        (services.GetService(typeof(SemanticModuleRuntime)) as SemanticModuleRuntime)
            ?.ReportComposition(builder, services);

        builder.ApplyTo(out var elections, out var capabilities, out var configKeys, out var entities, out var facts);
        var referenceManifest = services.GetService(typeof(KoanApplicationReferenceManifest))
            as KoanApplicationReferenceManifest;
        var directReferences = referenceManifest?.IsPresent == true
            ? referenceManifest.DirectReferences
                .Select(reference => new KoanLockReference(
                    reference.Kind == KoanReferenceKind.Package ? "package" : "project",
                    reference.RawIdentity))
                .ToArray()
            : null;
        var lockfile = new KoanLockfile(
            KoanLockfile.CurrentSchema,
            app,
            lockModules,
            DirectReferences: directReferences,
            Elections: elections,
            Capabilities: capabilities,
            ConfigKeys: configKeys,
            Entities: entities);
        return new KoanCompositionResult(lockfile, facts);
    }

    /// <summary>Build the resolved twin from a live host (app name from <see cref="KoanEnv"/>).</summary>
    public static KoanLockfile BuildFromServices(IServiceProvider services)
    {
        var appName = KoanEnv.CurrentSnapshot.Application.Name;
        var config = services.GetService(typeof(IConfiguration)) as IConfiguration;
        return Build(services, appName, config);
    }

    internal static IReadOnlyList<KoanLockModule> GetLoadedKoanModules(Assembly? applicationAssembly)
    {
        var applicationAssemblyName = applicationAssembly?.GetName().Name;
        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .Select(a => a.GetName())
            .Where(n => !string.IsNullOrEmpty(n.Name)
                && !string.Equals(n.Name, applicationAssemblyName, StringComparison.Ordinal)
                && (n.Name!.StartsWith("Koan.", StringComparison.Ordinal) || n.Name == "Koan")
                && !n.Name.EndsWith(".Generators", StringComparison.Ordinal)
                && !n.Name.StartsWith("Koan.Testing", StringComparison.Ordinal))
            .Select(n => new KoanLockModule(n.Name!, MajorMinor(n.Version?.ToString())))
            .GroupBy(m => m.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(m => m.Id, StringComparer.Ordinal)
            .ToArray();
    }

    internal static string ResolveApplicationName(Assembly? applicationAssembly, string? fallback)
    {
        var assemblyName = applicationAssembly?.GetName().Name;
        if (!string.IsNullOrWhiteSpace(assemblyName)) return assemblyName;
        return string.IsNullOrWhiteSpace(fallback) ? "app" : fallback;
    }

    private static Assembly? ResolveApplicationAssembly(IServiceProvider services)
    {
        var applicationName = (services.GetService(typeof(IHostEnvironment)) as IHostEnvironment)?.ApplicationName;
        if (!string.IsNullOrWhiteSpace(applicationName))
        {
            var applicationAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => string.Equals(
                    assembly.GetName().Name,
                    applicationName,
                    StringComparison.Ordinal));
            if (applicationAssembly is not null) return applicationAssembly;
        }

        return Assembly.GetEntryAssembly();
    }

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
