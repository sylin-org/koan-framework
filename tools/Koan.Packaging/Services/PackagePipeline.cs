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

    public async Task PackAndVerifyAsync(
        ReleaseManifest manifest,
        string outputDirectory,
        bool cleanRoom,
        bool resume,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        if (!resume)
        {
            foreach (var file in Directory.EnumerateFiles(outputDirectory, "*.nupkg")) File.Delete(file);
            foreach (var file in Directory.EnumerateFiles(outputDirectory, "*.snupkg")) File.Delete(file);
        }

        foreach (var package in manifest.Packages.Where(package => !package.AlreadyPublished))
        {
            var artifact = resume ? FindPackage(outputDirectory, package.PackageId, package.Version) : null;
            if (artifact is null)
            {
                Console.WriteLine($"pack   {package.PackageId} {package.Version}");
                await processRunner.RequireAsync(
                    "dotnet",
                    [
                        "pack", package.ProjectPath, "-c", "Release", "--nologo",
                        "-p:PublicRelease=true", "-p:NuGetAudit=true", "-p:NuGetAuditMode=all",
                        "-p:NuGetAuditLevel=high", "-p:WarningsAsErrors=NU1903%3BNU1904",
                        "-o", outputDirectory
                    ],
                    repositoryRoot,
                    cancellationToken,
                    echo: true);

                artifact = FindPackage(outputDirectory, package.PackageId, package.Version)
                    ?? throw new InvalidOperationException($"Pack succeeded but {package.Identity} was not produced.");
            }
            else
            {
                Console.WriteLine($"reuse  {package.PackageId} {package.Version}");
            }
            var inspected = InspectPackage(artifact);
            ValidateMetadata(package, inspected, manifest.SourceCommit);
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
    }

    public async Task PublishAsync(
        ReleaseManifest manifest,
        string artifactDirectory,
        string apiKey,
        string statePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("NuGet publishing credential is empty.");
        var state = File.Exists(statePath)
            ? JsonSerializer.Deserialize<ReleaseState>(await File.ReadAllTextAsync(statePath, cancellationToken), JsonOptions)
            : null;
        state ??= new ReleaseState { SourceCommit = manifest.SourceCommit };
        if (!string.Equals(state.SourceCommit, manifest.SourceCommit, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Release state belongs to a different source commit.");
        }

        foreach (var package in manifest.Packages)
        {
            if (state.Packages.TryGetValue(package.Identity, out var disposition) &&
                string.Equals(disposition, "available", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (package.AlreadyPublished)
            {
                Console.WriteLine($"exists  {package.Identity}");
                state.Packages[package.Identity] = "available";
                await SaveStateAsync(state, statePath, cancellationToken);
                continue;
            }

            if (package.PackageFile is null) throw new InvalidOperationException($"{package.Identity} has no package artifact in the manifest.");
            var packagePath = Path.Combine(artifactDirectory, package.PackageFile);
            await VerifyArtifactHashAsync(packagePath, package.PackageSha256, package.Identity, cancellationToken);
            string? symbolsPath = null;
            if (package.SymbolsFile is not null)
            {
                symbolsPath = Path.Combine(artifactDirectory, package.SymbolsFile);
                await VerifyArtifactHashAsync(symbolsPath, package.SymbolsSha256, $"{package.Identity} symbols", cancellationToken);
            }

            var packageExists = await registry.ExistsAsync(package.PackageId, package.Version, cancellationToken);
            if (!packageExists)
            {
                Console.WriteLine($"push    {package.Identity}");
                await PushWithRetryAsync(packagePath, apiKey, cancellationToken);
            }
            else
            {
                Console.WriteLine($"resume  {package.Identity} package exists; reconciling symbols/state");
            }
            if (symbolsPath is not null)
            {
                // A package push may have succeeded before a symbol push failed. Replaying the
                // symbol artifact with --skip-duplicate closes that partial-publication window.
                await PushWithRetryAsync(symbolsPath, apiKey, cancellationToken);
            }

            await WaitUntilAvailableAsync(package.PackageId, package.Version, cancellationToken);
            state.Packages[package.Identity] = "available";
            await SaveStateAsync(state, statePath, cancellationToken);
        }
    }

    public static async Task SaveManifestAsync(ReleaseManifest manifest, string path, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(manifest, JsonOptions) + Environment.NewLine, cancellationToken);
    }

    public static async Task<ReleaseManifest> LoadManifestAsync(string path, CancellationToken cancellationToken) =>
        JsonSerializer.Deserialize<ReleaseManifest>(await File.ReadAllTextAsync(path, cancellationToken), JsonOptions)
        ?? throw new InvalidOperationException($"Unable to deserialize release manifest '{path}'.");

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
    {
        var value = range.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (!value.StartsWith("[", StringComparison.Ordinal) ||
            !value.EndsWith(")", StringComparison.Ordinal)) return false;
        var parts = value[1..^1].Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !Version.TryParse(parts[0], out var minimum) ||
            !Version.TryParse(parts[1], out var maximum) ||
            minimum.Build < 0 || maximum.Build < 0) return false;
        var expectedMaximum = minimum.Major == 0
            ? new Version(0, minimum.Minor + 1, 0)
            : new Version(minimum.Major + 1, 0, 0);
        return maximum == expectedMaximum;
    }

    private async Task VerifyClosureAsync(ReleaseManifest manifest, CancellationToken cancellationToken)
    {
        var selected = manifest.Packages.ToDictionary(package => package.PackageId, StringComparer.OrdinalIgnoreCase);
        foreach (var package in manifest.Packages.Where(package => !package.AlreadyPublished))
        {
            foreach (var dependency in package.PackageDependencies.Where(IsKoanPackage))
            {
                if (!IsExpectedCompatibilityBand(dependency.VersionRange))
                {
                    throw new InvalidOperationException(
                        $"{package.Identity} requires {dependency.PackageId} with non-canonical range '{dependency.VersionRange}'. " +
                        "Internal dependencies must use Koan's closed-open compatibility band.");
                }
                var minimum = dependency.MinimumVersion
                    ?? throw new InvalidOperationException($"{package.Identity} has an unbounded internal dependency on {dependency.PackageId}.");
                if (selected.TryGetValue(dependency.PackageId, out var selectedDependency) &&
                    string.Equals(selectedDependency.Version, minimum, StringComparison.OrdinalIgnoreCase))
                {
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
        Console.WriteLine($"prove   application packages app={appVersion} sqlite={sqliteVersion} jobs={jobsVersion} mcp={mcpVersion}");
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
            await RestoreAndBuildCleanRoomApplicationAsync(
                firstUseApp,
                PackagingConstants.FirstUse.ProjectFile,
                nugetConfig,
                packageProperties,
                cancellationToken);
            var firstUseEvidence = await new FirstUseApplicationProbe().RunBuiltAsync(firstUseApp, "package", cancellationToken);
            var firstUseEvidencePath = EvidencePath(artifactDirectory, PackagingConstants.FirstUse.EvidenceFileName);
            await WriteEvidenceAsync(firstUseEvidencePath, firstUseEvidence, cancellationToken);
            Console.WriteLine($"proved  FirstUse package path in {firstUseEvidence.TotalSeconds:0.000}s; evidence={firstUseEvidencePath}");

            await RestoreAndBuildCleanRoomApplicationAsync(
                goldenJourneyApp,
                PackagingConstants.GoldenJourney.ProjectFile,
                nugetConfig,
                packageProperties,
                cancellationToken);
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

    private async Task RestoreAndBuildCleanRoomApplicationAsync(
        string applicationDirectory,
        string projectFile,
        string nugetConfig,
        IReadOnlyCollection<string> packageProperties,
        CancellationToken cancellationToken)
    {
        await processRunner.RequireAsync(
            "dotnet",
            new[]
            {
                "restore", projectFile, "--configfile", nugetConfig, "--no-cache", "--force-evaluate"
            }.Concat(packageProperties).Concat(new[]
            {
                "-p:NuGetAuditMode=all", "-p:NuGetAuditLevel=high", "-p:WarningsAsErrors=NU1903%3BNU1904"
            }),
            applicationDirectory,
            cancellationToken,
            echo: true);
        await processRunner.RequireAsync(
            "dotnet",
            new[] { "build", projectFile, "-c", "Release", "--no-restore", "--nologo" }
                .Concat(packageProperties),
            applicationDirectory,
            cancellationToken,
            echo: true);
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

    private async Task PushWithRetryAsync(string packagePath, string apiKey, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= PackagingConstants.PublishAttempts; attempt++)
        {
            var result = await processRunner.RunAsync(
                "dotnet",
                ["nuget", "push", packagePath, "--source", PackagingConstants.NuGetSource, "--api-key", apiKey, "--skip-duplicate", "--timeout", "300"],
                repositoryRoot,
                cancellationToken,
                echo: true);
            if (result.ExitCode == 0) return;
            if (attempt == PackagingConstants.PublishAttempts) throw new InvalidOperationException($"Failed to publish {Path.GetFileName(packagePath)} after {attempt} attempts.");
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
        }
    }

    private async Task WaitUntilAvailableAsync(string packageId, string version, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < PackagingConstants.RegistryAttempts; attempt++)
        {
            if (await registry.ExistsAsync(packageId, version, cancellationToken)) return;
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 2 + attempt * 2)), cancellationToken);
        }
        throw new InvalidOperationException($"Published package {packageId}/{version} did not become available before the registry timeout.");
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

    private static async Task VerifyArtifactHashAsync(
        string path,
        string? expectedHash,
        string identity,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) throw new InvalidOperationException($"Artifact for {identity} is missing: {path}");
        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            throw new InvalidOperationException($"Manifest has no SHA-256 for {identity}.");
        }
        var actualHash = await HashAsync(path, cancellationToken);
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Artifact hash mismatch for {identity}: expected {expectedHash}, found {actualHash}.");
        }
    }

    private static async Task SaveStateAsync(ReleaseState state, string path, CancellationToken cancellationToken) =>
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(state, JsonOptions) + Environment.NewLine, cancellationToken);

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
