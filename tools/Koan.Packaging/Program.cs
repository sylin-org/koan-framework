using System.Text.Json;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;
using Koan.Packaging.Services;

return await PackagingProgram.RunAsync(args);

internal static class PackagingProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var cancellationToken = CancellationToken.None;
            var root = FindRepositoryRoot(Environment.CurrentDirectory);
            var process = new ProcessRunner();
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Koan.Packaging/1.0");
            var registry = new NuGetRegistry(http);
            var repository = new RepositoryInspector(root, process);
            var productSurfaceCompiler = new ProductSurfaceCompiler(root);
            var packageQualityCompiler = new PackageQualityCompiler(root);
            var lineageCompiler = new ReleaseLineageCompiler(root, process, repository);
            var planner = new ReleasePlanner(repository, registry, productSurfaceCompiler);
            var pipeline = new PackagePipeline(root, process, registry);
            var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "help";
            var options = CommandOptions.Parse(args.Skip(1));

            switch (command)
            {
                case "quality":
                {
                    var packages = await repository.DiscoverPackagesAsync(cancellationToken);
                    var report = packageQualityCompiler.Compile(packages);
                    var output = options.Value("output");
                    await WriteOutputAsync(
                        output,
                        PackageQualityCompiler.ToJson(report).TrimEnd(),
                        cancellationToken);
                    var markdown = options.Value("markdown");
                    if (markdown is not null)
                    {
                        await WriteOutputAsync(
                            markdown,
                            PackageQualityCompiler.ToMarkdown(report).TrimEnd(),
                            cancellationToken);
                    }
                    var status = $"quality    {report.Summary.Packages} package(s), " +
                                 $"{report.Summary.RepairRequired} repair, " +
                                 $"{report.Summary.ReviewRequired} review, " +
                                 $"{report.Summary.StructurallyReady} structurally ready";
                    if (output is null) Console.Error.WriteLine(status); else Console.WriteLine(status);
                    return 0;
                }
                case "product-surface":
                {
                    var packages = await repository.DiscoverPackagesAsync(cancellationToken);
                    var surface = await productSurfaceCompiler.CompileAsync(packages, cancellationToken);
                    var output = options.Value("output");
                    await WriteOutputAsync(
                        output,
                        ProductSurfaceCompiler.ToJson(surface).TrimEnd(),
                        cancellationToken);
                    var markdown = options.Value("markdown");
                    if (markdown is not null)
                    {
                        await WriteOutputAsync(
                            markdown,
                            ProductSurfaceCompiler.ToMarkdown(surface).TrimEnd(),
                            cancellationToken);
                    }
                    var status = $"surface    {surface.Claims.Count} claim(s), {surface.Packages.Count} package(s)";
                    if (output is null) Console.Error.WriteLine(status); else Console.WriteLine(status);
                    return 0;
                }
                case "inventory":
                {
                    var packages = await repository.DiscoverPackagesAsync(cancellationToken);
                    var json = JsonSerializer.Serialize(packages, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
                    await WriteOutputAsync(options.Value("output"), json, cancellationToken);
                    Console.WriteLine($"inventory  {packages.Count} independently versioned package project(s)");
                    return 0;
                }
                case "plan":
                {
                    var lineagePath = options.Value("lineage")
                        ?? Path.Combine(root, "artifacts", "release", PackagingConstants.LineageArtifactFileName);
                    var lineage = await ReleaseLineageCompiler.LoadAsync(lineagePath, cancellationToken);
                    var manifest = await planner.CreateAsync(lineage, options.Has("offline"), cancellationToken);
                    var output = options.Value("output") ?? Path.Combine(root, "artifacts", "release", PackagingConstants.ManifestFileName);
                    await PackagePipeline.SaveManifestAsync(manifest, output, cancellationToken);
                    PrintPlan(manifest, output);
                    return 0;
                }
                case "lineage":
                {
                    var lineage = await lineageCompiler.CompileAsync(
                        options.Value("source") ?? PackagingConstants.DefaultAfterRevision,
                        options.Value("previous-source") ?? PackagingConstants.DefaultBeforeRevision,
                        options.Value("branch") ?? PackagingConstants.DefaultLineageBranch,
                        options.Value("previous-lineage"),
                        cancellationToken);
                    var output = options.Value("output")
                        ?? Path.Combine(root, "artifacts", "release", PackagingConstants.LineageArtifactFileName);
                    await ReleaseLineageCompiler.SaveAsync(lineage, output, cancellationToken);
                    PrintLineage(lineage, output);
                    return 0;
                }
                case "materialize-lineage":
                {
                    var lineage = await lineageCompiler.MaterializeCommittedAsync(
                        Require(options, "version-commit"),
                        cancellationToken);
                    var output = options.Value("output")
                        ?? Path.Combine(root, "artifacts", "release", PackagingConstants.LineageArtifactFileName);
                    await ReleaseLineageCompiler.SaveAsync(lineage, output, cancellationToken);
                    PrintLineage(lineage, output);
                    return 0;
                }
                case "pack":
                {
                    var manifestPath = Require(options, "manifest");
                    var output = options.Value("output") ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifestPath))!, "packages");
                    var manifest = await PackagePipeline.LoadManifestAsync(manifestPath, cancellationToken);
                    await pipeline.PackAndVerifyAsync(
                        manifest,
                        output,
                        options.Has("clean-room"),
                        options.Has("resume"),
                        cancellationToken);
                    await PackagePipeline.SaveManifestAsync(manifest, manifestPath, cancellationToken);
                    Console.WriteLine($"verified  {manifest.Packages.Count} release artifact(s)");
                    return 0;
                }
                case "wave-bundle":
                {
                    var manifestPath = Require(options, "manifest");
                    var releaseDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
                    var marker = await ReleaseWaveBundle.PrepareAsync(
                        new ReleaseWavePreparation(
                            options.Value("lineage") ?? Path.Combine(releaseDirectory, PackagingConstants.LineageArtifactFileName),
                            manifestPath,
                            options.Value("artifacts") ?? Path.Combine(releaseDirectory, "packages"),
                            options.Value("evidence") ?? releaseDirectory,
                            options.Value("output") ?? releaseDirectory),
                        cancellationToken);
                    Console.WriteLine($"prepared  {marker.TagName}");
                    Console.WriteLine($"bundle    {marker.Bundle.FileName} {marker.Bundle.Sha256}");
                    return 0;
                }
                case "wave-inspect":
                {
                    var coordinator = CreateReleaseWaveCoordinator(
                        root,
                        options,
                        process,
                        registry,
                        apiKey: null);
                    var inspection = await coordinator.InspectAsync(
                        Require(options, "version-commit"),
                        cancellationToken);
                    await WriteInspectionAsync(
                        inspection,
                        options.Value("output"),
                        cancellationToken);
                    return 0;
                }
                case "wave-stage":
                {
                    var coordinator = CreateReleaseWaveCoordinator(
                        root,
                        options,
                        process,
                        registry,
                        apiKey: null);
                    var inspection = await coordinator.StageAsync(
                        Require(options, "marker"),
                        cancellationToken);
                    await WriteInspectionAsync(
                        inspection,
                        options.Value("output"),
                        cancellationToken);
                    return 0;
                }
                case "wave-promote":
                {
                    var apiKeyVariable = options.Value("api-key-env") ?? "NUGET_API_KEY";
                    var coordinator = CreateReleaseWaveCoordinator(
                        root,
                        options,
                        process,
                        registry,
                        Environment.GetEnvironmentVariable(apiKeyVariable));
                    var inspection = await coordinator.PromoteAsync(
                        Require(options, "version-commit"),
                        cancellationToken);
                    await WriteInspectionAsync(
                        inspection,
                        options.Value("output"),
                        cancellationToken);
                    return 0;
                }
                case "help":
                case "--help":
                case "-h":
                    PrintHelp();
                    return 0;
                default:
                    throw new InvalidOperationException($"Unknown command '{command}'. Run with 'help' for supported commands.");
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"error  {exception.Message}");
            return 1;
        }
    }

    private static void PrintPlan(ReleaseManifest manifest, string output)
    {
        Console.WriteLine($"release  {manifest.PreviousVersionCommit[..12]} -> {manifest.VersionCommit[..12]}");
        Console.WriteLine($"source   {manifest.SourceCommit}");
        foreach (var package in manifest.Packages)
        {
            var disposition = package.AlreadyPublished ? "repair" : "mint";
            Console.WriteLine($"{disposition,-7} {package.PackageId} {package.PreviousVersion ?? "new"} -> {package.Version} ({package.Reason})");
        }
        Console.WriteLine($"manifest {Path.GetFullPath(output)}");
    }

    private static void PrintLineage(ReleaseLineage lineage, string output)
    {
        Console.WriteLine($"lineage  {lineage.PreviousVersionCommit[..12]} -> {lineage.VersionCommit[..12]}");
        Console.WriteLine($"source   {lineage.SourceCommit}");
        Console.WriteLine($"closure  {lineage.ClosurePackages.Count} package(s), {lineage.MarkerPackages.Count} generated marker(s)");
        Console.WriteLine($"artifact {Path.GetFullPath(output)}");
    }

    private static void PrintHelp() => Console.WriteLine("""
        Koan package release compiler

          inventory [--output PATH]
          quality   [--output PATH] [--markdown PATH]
          product-surface [--output PATH] [--markdown PATH]
          lineage   [--source GIT] [--previous-source GIT] [--previous-lineage GIT] [--branch NAME] [--output PATH]
          materialize-lineage --version-commit GIT [--output PATH]
          plan      [--lineage PATH] [--output PATH] [--offline]
          pack      --manifest PATH [--output DIR] [--clean-room] [--resume]
          wave-bundle --manifest PATH [--lineage PATH] [--artifacts DIR] [--evidence DIR] [--output DIR]
          wave-inspect --version-commit GIT [--repository OWNER/REPO] [--scratch DIR] [--output PATH]
          wave-stage --marker PATH [--repository OWNER/REPO] [--scratch DIR] [--output PATH]
          wave-promote --version-commit GIT [--repository OWNER/REPO] [--scratch DIR] [--api-key-env NAME] [--output PATH]
        """);

    private static ReleaseWaveCoordinator CreateReleaseWaveCoordinator(
        string root,
        CommandOptions options,
        ProcessRunner process,
        NuGetRegistry registry,
        string? apiKey)
    {
        var repository = options.Value("repository")
            ?? Environment.GetEnvironmentVariable("GITHUB_REPOSITORY")
            ?? throw new InvalidOperationException(
                "--repository OWNER/REPO is required outside GitHub Actions (GITHUB_REPOSITORY is not set).");
        var scratch = options.Value("scratch")
            ?? Path.Combine(root, "artifacts", "release", "wave-scratch");
        return new ReleaseWaveCoordinator(
            new GitHubReleaseWaveEscrow(root, repository, process),
            new NuGetPackagePromotionTarget(root, apiKey, process, registry),
            scratch);
    }

    private static async Task WriteInspectionAsync(
        ReleaseWaveInspection inspection,
        string? output,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(
            inspection,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        await WriteOutputAsync(output, json, cancellationToken);
        Console.WriteLine($"wave      {inspection.State} {inspection.TagName}");
        if (output is not null) Console.WriteLine($"inspection {Path.GetFullPath(output)}");
    }

    private static string Require(CommandOptions options, string name) =>
        options.Value(name) ?? throw new InvalidOperationException($"--{name} is required.");

    private static async Task WriteOutputAsync(string? path, string content, CancellationToken cancellationToken)
    {
        if (path is null) { Console.WriteLine(content); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(path, content + Environment.NewLine, cancellationToken);
    }

    private static string FindRepositoryRoot(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git"))) return directory.FullName;
        }
        throw new InvalidOperationException("Run Koan.Packaging from inside the Koan repository.");
    }
}

internal sealed class CommandOptions
{
    private readonly Dictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase);

    public static CommandOptions Parse(IEnumerable<string> arguments)
    {
        var result = new CommandOptions();
        var input = arguments.ToArray();
        for (var index = 0; index < input.Length; index++)
        {
            var argument = input[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal)) throw new InvalidOperationException($"Unexpected argument '{argument}'.");
            var name = argument[2..];
            string? value = null;
            if (index + 1 < input.Length && !input[index + 1].StartsWith("--", StringComparison.Ordinal)) value = input[++index];
            result.values[name] = value;
        }
        return result;
    }

    public bool Has(string name) => values.ContainsKey(name);
    public string? Value(string name) => values.GetValueOrDefault(name);
}
