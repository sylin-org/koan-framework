using System.IO.Compression;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;

namespace Koan.Packaging.Services;

internal sealed class PackagePipeline(
    string repositoryRoot,
    ProcessRunner processRunner,
    NuGetRegistry registry)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly Guid SourceLinkKind = new("CC110556-A091-4D38-9FEC-25AB9A351A6A");
    private readonly CleanRoomApplicationCompiler applicationCompiler = new(processRunner);
    private static readonly string[] RequiredCoreBuildAssetPackagePaths =
    [
        PackagingConstants.CoreCompositionTargetPackagePath,
        PackagingConstants.CoreSemanticActivationTargetPackagePath,
        PackagingConstants.CoreRegistryGeneratorPackagePath
    ];

    public async Task PackAndVerifyAsync(
        ReleaseManifest manifest,
        string outputDirectory,
        bool cleanRoom,
        bool resume,
        CancellationToken cancellationToken)
    {
        await RequireVersionCommitCheckoutAsync(manifest.VersionCommit, cancellationToken);
        await RequireCleanPackageInputsAsync(cancellationToken);
        Directory.CreateDirectory(outputDirectory);
        if (!resume)
        {
            foreach (var file in Directory.EnumerateFiles(outputDirectory, "*.nupkg")) File.Delete(file);
            foreach (var file in Directory.EnumerateFiles(outputDirectory, "*.snupkg")) File.Delete(file);
        }

        foreach (var package in manifest.Packages)
        {
            var artifact = resume ? FindPackage(outputDirectory, package.PackageId, package.Version) : null;
            if (artifact is null)
            {
                Console.WriteLine($"pack   {package.PackageId} {package.Version}");
                PreparedTemplatePackage? preparedTemplate = null;
                try
                {
                    var arguments = new List<string>
                    {
                        "pack", package.ProjectPath, "-c", "Release", "--nologo",
                        "-p:PublicRelease=true", "-p:NuGetAudit=true", "-p:NuGetAuditMode=all",
                        "-p:NuGetAuditLevel=high", "-p:WarningsAsErrors=NU1903%3BNU1904",
                        "-o", outputDirectory
                    };
                    if (string.Equals(
                            package.PackageId,
                            PackagingConstants.TemplatePackage.PackageId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        preparedTemplate = await new TemplatePackageCompiler(repositoryRoot).PrepareAsync(
                            await ResolveTemplateVersionsAsync(manifest, cancellationToken),
                            cancellationToken);
                        arguments.Add($"-p:KoanPreparedTemplateRoot={preparedTemplate.Root}");
                    }
                    await processRunner.RequireAsync(
                        "dotnet",
                        arguments,
                        repositoryRoot,
                        cancellationToken,
                        echo: true);
                }
                finally
                {
                    preparedTemplate?.Dispose();
                }

                artifact = FindPackage(outputDirectory, package.PackageId, package.Version)
                    ?? throw new InvalidOperationException($"Pack succeeded but {package.Identity} was not produced.");
            }
            else
            {
                Console.WriteLine($"reuse  {package.PackageId} {package.Version}");
            }
            var inspected = InspectPackage(artifact);
            ValidateMetadata(package, inspected, manifest.VersionCommit);
            ValidateRequiredBuildAssets(package.PackageId, artifact);
            package.PackageDependencies = inspected.Dependencies;
            package.PackageFile = Path.GetFileName(artifact);
            package.PackageSha256 = await HashAsync(artifact, cancellationToken);

            var symbols = Path.Combine(outputDirectory, $"{package.PackageId}.{package.Version}.snupkg");
            if (package.IncludeSymbols && !File.Exists(symbols))
            {
                throw new InvalidOperationException($"{package.Identity} requires a symbol package, but none was produced.");
            }
            if (File.Exists(symbols))
            {
                ValidateSymbolPackage(symbols, package.Identity);
                package.SymbolsFile = Path.GetFileName(symbols);
                package.SymbolsSha256 = await HashAsync(symbols, cancellationToken);
            }
        }

        await VerifyClosureAsync(manifest, cancellationToken);
        if (cleanRoom) await VerifyCleanRoomAsync(manifest, outputDirectory, cancellationToken);
        await RequireCleanPackageInputsAsync(cancellationToken);
    }

    private async Task<IReadOnlyDictionary<string, string>> ResolveTemplateVersionsAsync(
        ReleaseManifest manifest,
        CancellationToken cancellationToken)
    {
        var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var packageId in PackagingConstants.TemplatePackage.RequiredPackageIds)
        {
            versions[packageId] = manifest.Packages.FirstOrDefault(package =>
                                      string.Equals(package.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
                                  ?.Version
                              ?? await registry.GetLatestStableVersionAsync(packageId, cancellationToken);
        }
        return versions;
    }

    public static async Task SaveManifestAsync(ReleaseManifest manifest, string path, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(manifest, JsonOptions) + Environment.NewLine, cancellationToken);
    }

    public static async Task<ReleaseManifest> LoadManifestAsync(string path, CancellationToken cancellationToken)
    {
        var manifest = JsonSerializer.Deserialize<ReleaseManifest>(
            await File.ReadAllTextAsync(path, cancellationToken),
            JsonOptions)
            ?? throw new InvalidOperationException($"Unable to deserialize release manifest '{path}'.");
        if (manifest.SchemaVersion != PackagingConstants.ReleaseManifestSchema)
        {
            throw new InvalidOperationException(
                $"Release manifest '{path}' uses schema {manifest.SchemaVersion}; expected {PackagingConstants.ReleaseManifestSchema}.");
        }
        return manifest;
    }

    internal static string? MinimumVersion(string range)
    {
        var value = range.Trim();
        if (value.Length == 0) return null;
        if (value[0] is '[' or '(')
        {
            var comma = value.IndexOf(',');
            var minimum = (comma >= 0 ? value[1..comma] : value[1..^1]).Trim();
            return minimum.Length == 0 ? null : minimum;
        }
        return value;
    }

    internal static bool IsExpectedCompatibilityBand(string range)
        => PackageCompatibility.TryParseRange(range, out _);

    internal static void ValidateRequiredBuildAssets(string packageId, string packagePath)
    {
        if (!string.Equals(packageId, PackagingConstants.CorePackageId, StringComparison.OrdinalIgnoreCase)) return;

        using var archive = ZipFile.OpenRead(packagePath);
        var packageEntries = archive.Entries
            .Select(entry => entry.FullName)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var requiredPath in RequiredCoreBuildAssetPackagePaths)
        {
            if (packageEntries.Contains(requiredPath)) continue;

            throw new InvalidOperationException(
                $"{packageId} is missing required transitive build asset '{requiredPath}'.");
        }
    }

    internal async Task VerifyClosureAsync(ReleaseManifest manifest, CancellationToken cancellationToken)
    {
        var selected = manifest.Packages.ToDictionary(package => package.PackageId, StringComparer.OrdinalIgnoreCase);
        foreach (var package in manifest.Packages)
        {
            var expectedDependencies = package.ProjectDependencies.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var packageDependencies = package.PackageDependencies.Where(IsKoanPackage).ToArray();
            var actualDependencies = packageDependencies
                .Select(dependency => dependency.PackageId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!expectedDependencies.SetEquals(actualDependencies))
            {
                var missing = expectedDependencies.Except(actualDependencies, StringComparer.OrdinalIgnoreCase);
                var unexpected = actualDependencies.Except(expectedDependencies, StringComparer.OrdinalIgnoreCase);
                throw new InvalidOperationException(
                    $"{package.Identity} internal dependency closure differs from its evaluated project graph; " +
                    $"missing=[{string.Join(", ", missing)}], unexpected=[{string.Join(", ", unexpected)}].");
            }

            foreach (var dependency in packageDependencies)
            {
                if (!IsExpectedCompatibilityBand(dependency.VersionRange))
                {
                    throw new InvalidOperationException(
                        $"{package.Identity} requires {dependency.PackageId} with non-canonical range '{dependency.VersionRange}'. " +
                        "Internal dependencies must use Koan's closed-open compatibility band.");
                }
                var minimum = dependency.MinimumVersion
                    ?? throw new InvalidOperationException($"{package.Identity} has an unbounded internal dependency on {dependency.PackageId}.");
                if (selected.TryGetValue(dependency.PackageId, out var selectedDependency))
                {
                    if (!string.Equals(selectedDependency.Version, minimum, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"{package.Identity} requires selected dependency {dependency.PackageId}/{minimum}, " +
                            $"but this release set contains {selectedDependency.Identity}.");
                    }
                    continue;
                }
                if (!await registry.ExistsAsync(dependency.PackageId, minimum, cancellationToken))
                {
                    throw new InvalidOperationException(
                        $"{package.Identity} requires unpublished {dependency.PackageId}/{minimum}, and it is absent from this release set.");
                }
            }
        }
    }

    private async Task VerifyCleanRoomAsync(ReleaseManifest manifest, string artifactDirectory, CancellationToken cancellationToken)
    {
        var firstUse = Path.Combine(repositoryRoot, "samples", "FirstUse");
        var goldenJourney = Path.Combine(repositoryRoot, "samples", "GoldenJourney");
        if (!Directory.Exists(firstUse)) throw new InvalidOperationException($"FirstUse application is missing: {firstUse}");
        if (!Directory.Exists(goldenJourney)) throw new InvalidOperationException($"GoldenJourney application is missing: {goldenJourney}");
        var root = Path.Combine(Path.GetTempPath(), $"koan-package-cleanroom-{Guid.NewGuid():N}");
        var feed = Path.Combine(root, "feed");
        var firstUseApp = Path.Combine(root, "first-use");
        var goldenJourneyApp = Path.Combine(root, "golden-journey");
        var globalPackages = Path.Combine(root, "packages");
        Directory.CreateDirectory(feed);
        CopyDirectory(firstUse, firstUseApp);
        CopyDirectory(goldenJourney, goldenJourneyApp);
        foreach (var artifact in Directory.EnumerateFiles(artifactDirectory, "*.nupkg"))
        {
            File.Copy(artifact, Path.Combine(feed, Path.GetFileName(artifact)), overwrite: true);
        }

        var appVersion = await EnsureRootPackageAsync(manifest, "Sylin.Koan.App", feed, cancellationToken);
        var sqliteVersion = await EnsureRootPackageAsync(manifest, "Sylin.Koan.Data.Connector.Sqlite", feed, cancellationToken);
        var mcpVersion = await EnsureRootPackageAsync(manifest, "Sylin.Koan.Mcp", feed, cancellationToken);
        var jobsVersion = await EnsureRootPackageAsync(manifest, "Sylin.Koan.Jobs", feed, cancellationToken);
        var templateVersion = await EnsureRootPackageAsync(
            manifest,
            PackagingConstants.TemplatePackage.PackageId,
            feed,
            cancellationToken);
        Console.WriteLine(
            $"prove   application packages app={appVersion} sqlite={sqliteVersion} jobs={jobsVersion} " +
            $"mcp={mcpVersion} templates={templateVersion}");
        await HydrateClosureAsync(feed, cancellationToken);
        var nugetConfig = Path.Combine(root, "NuGet.Config");
        await File.WriteAllTextAsync(nugetConfig, NuGetConfig(feed, globalPackages), cancellationToken);
        var packageProperties = new[]
        {
            "-p:UseKoanSource=false",
            $"-p:KoanAppVersion={appVersion}",
            $"-p:KoanSqliteVersion={sqliteVersion}",
            $"-p:KoanJobsVersion={jobsVersion}",
            $"-p:KoanMcpVersion={mcpVersion}"
        };

        try
        {
            var templatePackage = FindPackage(feed, PackagingConstants.TemplatePackage.PackageId, templateVersion)
                ?? throw new InvalidOperationException(
                    $"Clean-room feed does not contain {PackagingConstants.TemplatePackage.PackageId}/{templateVersion}.");
            await new TemplatePackageProbe(processRunner).VerifyAsync(
                templatePackage,
                root,
                nugetConfig,
                cancellationToken);
            Console.WriteLine($"proved  package-first web and console templates from {Path.GetFileName(templatePackage)}");

            await applicationCompiler.RestoreAndBuildAsync(
                firstUseApp,
                PackagingConstants.FirstUse.ProjectFile,
                nugetConfig,
                packageProperties,
                environment: null,
                cancellationToken: cancellationToken);
            var firstUseEvidence = await new FirstUseApplicationProbe().RunBuiltAsync(firstUseApp, "package", cancellationToken);
            var firstUseEvidencePath = EvidencePath(artifactDirectory, PackagingConstants.FirstUse.EvidenceFileName);
            await WriteEvidenceAsync(firstUseEvidencePath, firstUseEvidence, cancellationToken);
            Console.WriteLine($"proved  FirstUse package path in {firstUseEvidence.TotalSeconds:0.000}s; evidence={firstUseEvidencePath}");

            await applicationCompiler.RestoreAndBuildAsync(
                goldenJourneyApp,
                PackagingConstants.GoldenJourney.ProjectFile,
                nugetConfig,
                packageProperties,
                environment: null,
                cancellationToken: cancellationToken);
            var goldenJourneyEvidence = await new GoldenJourneyApplicationProbe().RunBuiltAsync(
                goldenJourneyApp,
                "package",
                cancellationToken);
            var goldenJourneyEvidencePath = EvidencePath(
                artifactDirectory,
                PackagingConstants.GoldenJourney.EvidenceFileName);
            await WriteEvidenceAsync(goldenJourneyEvidencePath, goldenJourneyEvidence, cancellationToken);
            Console.WriteLine($"proved  GoldenJourney package path in {goldenJourneyEvidence.TotalSeconds:0.000}s; evidence={goldenJourneyEvidencePath}");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static string EvidencePath(string artifactDirectory, string fileName) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(artifactDirectory))!, fileName);

    private static Task WriteEvidenceAsync(string path, object evidence, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(path, JsonSerializer.Serialize(evidence, JsonOptions) + Environment.NewLine, cancellationToken);

    private async Task<string> EnsureRootPackageAsync(ReleaseManifest manifest, string packageId, string feed, CancellationToken cancellationToken)
    {
        var selected = manifest.Packages.FirstOrDefault(package => string.Equals(package.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
        var version = selected?.Version ?? await registry.GetLatestStableVersionAsync(packageId, cancellationToken);
        if (FindPackage(feed, packageId, version) is null)
        {
            await registry.DownloadAsync(packageId, version, Path.Combine(feed, $"{packageId}.{version}.nupkg"), cancellationToken);
        }
        return version;
    }

    private async Task HydrateClosureAsync(string feed, CancellationToken cancellationToken)
    {
        var inspected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            var pending = Directory.EnumerateFiles(feed, "*.nupkg")
                .Where(path => inspected.Add(path))
                .ToArray();
            if (pending.Length == 0) break;
            foreach (var packagePath in pending)
            {
                var package = InspectPackage(packagePath);
                foreach (var dependency in package.Dependencies.Where(IsKoanPackage))
                {
                    var version = dependency.MinimumVersion
                        ?? throw new InvalidOperationException($"{package.PackageId}/{package.Version} has an unbounded dependency on {dependency.PackageId}.");
                    if (FindPackage(feed, dependency.PackageId, version) is not null) continue;
                    var destination = Path.Combine(feed, $"{dependency.PackageId}.{version}.nupkg");
                    await registry.DownloadAsync(dependency.PackageId, version, destination, cancellationToken);
                }
            }
        }
    }

    private async Task RequireVersionCommitCheckoutAsync(string versionCommit, CancellationToken cancellationToken)
    {
        var head = await processRunner.RequireAsync(
            "git",
            ["rev-parse", "--verify", "HEAD^{commit}"],
            repositoryRoot,
            cancellationToken);
        if (string.Equals(head, versionCommit, StringComparison.OrdinalIgnoreCase)) return;
        throw new InvalidOperationException(
            $"Package verification requires version commit {versionCommit}, but the checkout is {head}.");
    }

    private async Task RequireCleanPackageInputsAsync(CancellationToken cancellationToken)
    {
        string[] packageInputs =
        [
            "src", "packaging", "templates", "build",
            "Directory.Build.props", "Directory.Build.targets",
            "Directory.Packages.props", "Directory.Packages.targets",
            "global.json", ".editorconfig", ".config/dotnet-tools.json",
            "NuGet.Config", "README.md", "icon.png", "resources/image/0_2.jpg"
        ];
        var status = await processRunner.RequireAsync(
            "git",
            new[] { "status", "--porcelain", "--untracked-files=all", "--" }.Concat(packageInputs),
            repositoryRoot,
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(status))
        {
            throw new InvalidOperationException(
                $"Package inputs differ from the exact version commit:{Environment.NewLine}{status}");
        }
    }

    private static InspectedPackage InspectPackage(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var nuspec = archive.Entries.SingleOrDefault(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Package '{path}' has no nuspec.");
        using var stream = nuspec.Open();
        var document = XDocument.Load(stream);
        var metadata = document.Descendants().First(element => element.Name.LocalName == "metadata");
        string? Value(string name) => metadata.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value.Trim();
        var dependencies = metadata.Descendants()
            .Where(element => element.Name.LocalName == "dependency")
            .Select(element =>
            {
                var id = element.Attribute("id")?.Value ?? string.Empty;
                var range = element.Attribute("version")?.Value ?? string.Empty;
                return new PackageDependency(id, range, MinimumVersion(range));
            })
            .ToList();
        return new InspectedPackage(
            Value("id") ?? string.Empty,
            Value("version") ?? string.Empty,
            Value("description"),
            Value("license"),
            Value("readme"),
            Value("tags"),
            metadata.Elements().FirstOrDefault(element => element.Name.LocalName == "repository")?.Attribute("commit")?.Value,
            dependencies);
    }

    private static void ValidateMetadata(ReleasePackage expected, InspectedPackage actual, string sourceCommit)
    {
        var errors = new List<string>();
        if (!string.Equals(expected.PackageId, actual.PackageId, StringComparison.OrdinalIgnoreCase)) errors.Add($"ID is '{actual.PackageId}'");
        if (!string.Equals(expected.Version, actual.Version, StringComparison.OrdinalIgnoreCase)) errors.Add($"version is '{actual.Version}'");
        if (string.IsNullOrWhiteSpace(actual.Description)) errors.Add("description is missing");
        if (string.IsNullOrWhiteSpace(actual.License)) errors.Add("license is missing");
        if (string.IsNullOrWhiteSpace(actual.Readme)) errors.Add("README is missing");
        if (string.IsNullOrWhiteSpace(actual.PackageTags)) errors.Add("package tags are missing");
        if (actual.RepositoryCommit is null) errors.Add("repository commit metadata is missing");
        else if (!string.Equals(actual.RepositoryCommit, sourceCommit, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"repository commit is '{actual.RepositoryCommit}', expected '{sourceCommit}'");
        }
        if (errors.Count > 0) throw new InvalidOperationException($"Invalid package {expected.Identity}: {string.Join("; ", errors)}.");
    }

    private static string? FindPackage(string directory, string packageId, string version) =>
        Directory.EnumerateFiles(directory, "*.nupkg")
            .FirstOrDefault(path =>
            {
                try
                {
                    var inspected = InspectPackage(path);
                    return string.Equals(inspected.PackageId, packageId, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(inspected.Version, version, StringComparison.OrdinalIgnoreCase);
                }
                catch { return false; }
            });

    private static void ValidateSymbolPackage(string path, string identity)
    {
        using var archive = ZipFile.OpenRead(path);
        var pdbs = archive.Entries
            .Where(entry => entry.FullName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (pdbs.Length == 0)
        {
            throw new InvalidOperationException($"Symbol package for {identity} contains no portable PDB.");
        }

        foreach (var pdb in pdbs)
        {
            using var stream = pdb.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            buffer.Position = 0;
            using var provider = MetadataReaderProvider.FromPortablePdbStream(buffer);
            var reader = provider.GetMetadataReader();
            if (reader.CustomDebugInformation
                .Select(reader.GetCustomDebugInformation)
                .Any(information => reader.GetGuid(information.Kind) == SourceLinkKind))
            {
                return;
            }
        }

        throw new InvalidOperationException($"Symbol package for {identity} contains no SourceLink document.");
    }

    private static bool IsKoanPackage(PackageDependency dependency) =>
        dependency.PackageId.StartsWith(PackagingConstants.PackagePrefix, StringComparison.OrdinalIgnoreCase);

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var directory in Directory.EnumerateDirectories(source)
                     .Where(directory => Path.GetFileName(directory) is not "bin" and not "obj" and not ".koan" and not "mcp-sdk"))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static string NuGetConfig(string feed, string globalPackages) => $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <config>
            <add key="globalPackagesFolder" value="{{globalPackages}}" />
          </config>
          <packageSources>
            <clear />
            <add key="koan-release" value="{{feed}}" />
            <add key="nuget.org" value="{{PackagingConstants.NuGetSource}}" />
          </packageSources>
          <auditSources>
            <clear />
            <add key="nuget.org" value="https://data.nuget.org/v3/index.json" />
          </auditSources>
          <packageSourceMapping>
            <packageSource key="koan-release"><package pattern="Sylin.Koan*" /></packageSource>
            <packageSource key="nuget.org"><package pattern="*" /></packageSource>
          </packageSourceMapping>
        </configuration>
        """;

    private sealed record InspectedPackage(
        string PackageId,
        string Version,
        string? Description,
        string? License,
        string? Readme,
        string? PackageTags,
        string? RepositoryCommit,
        List<PackageDependency> Dependencies);
}
