using System.Text.Json;
using Koan.Packaging.Infrastructure;
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
            var lineageCompiler = new ReleaseLineageCompiler(root, process, repository);
            var planner = new ReleasePlanner(repository, registry);
            var pipeline = new PackagePipeline(root, process, registry);
            var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "help";
            var options = CommandOptions.Parse(args.Skip(1));

            switch (command)
            {
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
                case "publish":
                {
                    var manifestPath = Require(options, "manifest");
                    var artifacts = options.Value("artifacts") ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifestPath))!, "packages");
                    var state = options.Value("state") ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifestPath))!, PackagingConstants.StateFileName);
                    var credentialVariable = options.Value("api-key-env") ?? "NUGET_API_KEY";
                    var credential = Environment.GetEnvironmentVariable(credentialVariable)
                        ?? throw new InvalidOperationException($"Environment variable '{credentialVariable}' is not set.");
                    var manifest = await PackagePipeline.LoadManifestAsync(manifestPath, cancellationToken);
                    await pipeline.PublishAsync(manifest, artifacts, credential, state, cancellationToken);
                    Console.WriteLine(
                        $"published  release set {manifest.VersionCommit[..Math.Min(12, manifest.VersionCommit.Length)]}; " +
                        $"source={manifest.SourceCommit[..Math.Min(12, manifest.SourceCommit.Length)]}");
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

    private static void PrintPlan(Koan.Packaging.Models.ReleaseManifest manifest, string output)
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

    private static void PrintLineage(Koan.Packaging.Models.ReleaseLineage lineage, string output)
    {
        Console.WriteLine($"lineage  {lineage.PreviousVersionCommit[..12]} -> {lineage.VersionCommit[..12]}");
        Console.WriteLine($"source   {lineage.SourceCommit}");
        Console.WriteLine($"closure  {lineage.ClosurePackages.Count} package(s), {lineage.MarkerPackages.Count} generated marker(s)");
        Console.WriteLine($"artifact {Path.GetFullPath(output)}");
    }

    private static void PrintHelp() => Console.WriteLine("""
        Koan package release compiler

          inventory [--output PATH]
          lineage   [--source GIT] [--previous-source GIT] [--previous-lineage GIT] [--branch NAME] [--output PATH]
          materialize-lineage --version-commit GIT [--output PATH]
          plan      [--lineage PATH] [--output PATH] [--offline]
          pack      --manifest PATH [--output DIR] [--clean-room] [--resume]
          publish   --manifest PATH [--artifacts DIR] [--state PATH] [--api-key-env NAME]
        """);

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
