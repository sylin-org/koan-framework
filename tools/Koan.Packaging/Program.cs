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
            var processRunner = new ProcessRunner();
            var repository = new RepositoryInspector(root, processRunner);
            var productSurfaceCompiler = new ProductSurfaceCompiler(root);
            var generatedOutputVerifier = new GeneratedOutputVerifier(root);
            var packageQualityCompiler = new PackageQualityCompiler(root);
            var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "help";
            var options = CommandOptions.Parse(args.Skip(1));

            switch (command)
            {
                case "terminal-outcomes":
                {
                    var packages = await repository.DiscoverPackagesAsync(cancellationToken);
                    var surface = await productSurfaceCompiler.CompileAsync(packages, cancellationToken);
                    var report = await new TerminalOutcomeReconciler(root).ReconcileAsync(
                        packages,
                        surface,
                        options.Has("final"),
                        cancellationToken);
                    await WriteOutputAsync(options.Value("output"), ToJson(report), cancellationToken);
                    Console.WriteLine(
                        $"terminal-outcomes  {report.Resolved}/{report.BaselineOwners} resolved; " +
                        $"{report.ActiveSupported} active supported, {report.Removed} removed, " +
                        $"{report.Remaining} remaining" + (report.Final ? ", final" : ", partial"));
                    return 0;
                }
                case "native-admission":
                {
                    var baseCommit = await repository.ResolveCommitAsync(options.RequiredValue("base"), cancellationToken);
                    var candidate = await repository.ResolveCommitAsync(options.RequiredValue("candidate"), cancellationToken);
                    var head = await repository.ResolveCommitAsync("HEAD", cancellationToken);
                    NativeCandidateGuard.RequireExact(
                        baseCommit,
                        candidate,
                        head,
                        await repository.IsAncestorAsync(baseCommit, candidate, cancellationToken),
                        await repository.IsWorktreeCleanAsync(cancellationToken));

                    var configuration = options.Value("configuration") ?? "Release";
                    if (configuration is not ("Debug" or "Release"))
                        throw new InvalidOperationException("native-admission --configuration must be 'Debug' or 'Release'.");
                    var packages = await repository.DiscoverPackagesAsync(cancellationToken);
                    var surface = await productSurfaceCompiler.CompileAsync(packages, cancellationToken);
                    var changedPaths = await repository.GetChangedPathsAsync(baseCommit, candidate, cancellationToken);
                    var plan = new NativeAdmissionPlanner().Create(
                        baseCommit,
                        candidate,
                        changedPaths,
                        packages,
                        surface);
                    var admissionRunner = new AdmissionRunner(root, processRunner, new AdmissionResultValidator());
                    var report = await new NativeAdmissionExecutor(admissionRunner).RunAsync(
                        plan,
                        configuration,
                        options.Has("no-build"),
                        cancellationToken);
                    await WriteOutputAsync(options.Value("output"), ToJson(report), cancellationToken);
                    Console.WriteLine(
                        $"native-admission  {report.Verdict}; candidate {candidate}; " +
                        $"{plan.AffectedClaims.Count} affected claim(s), {plan.Cells.Count} required cell(s)");
                    if (report.Verdict is PackagingConstants.Admission.PassedVerdict
                        or PackagingConstants.Admission.NotApplicable) return 0;
                    foreach (var reason in report.Reasons) Console.Error.WriteLine($"error  {reason}");
                    return 1;
                }
                case "admission":
                {
                    var runner = new AdmissionRunner(root, processRunner, new AdmissionResultValidator());
                    var report = await runner.RunAsync(
                        options.RequiredValue("id"),
                        options.RequiredValue("project"),
                        options.RequiredValue("filter"),
                        options.Value("lane") ?? PackagingConstants.Admission.DeterministicLane,
                        options.Value("phase") ?? PackagingConstants.Admission.ExecutionPhase,
                        options.IntValue("deadline-seconds", PackagingConstants.Admission.DefaultDeadlineSeconds),
                        options.Value("configuration") ?? "Release",
                        options.Has("no-build"),
                        cancellationToken);
                    await WriteOutputAsync(options.Value("output"), ToJson(report), cancellationToken);
                    Console.WriteLine(
                        $"admission  {report.CellId} {report.Verdict}; " +
                        $"{report.Passed} passed, {report.Failed} failed, " +
                        $"{report.Skipped} skipped, {report.Unknown} unknown");
                    if (report.Verdict == PackagingConstants.Admission.PassedVerdict) return 0;
                    foreach (var reason in report.Reasons) Console.Error.WriteLine($"error  {reason}");
                    Console.Error.WriteLine($"reproduce  {report.ReproductionCommand}");
                    return 1;
                }
                case "admission-results":
                {
                    var cellId = options.RequiredValue("id");
                    var project = options.RequiredValue("project");
                    var filter = options.RequiredValue("filter");
                    var lane = options.Value("lane") ?? PackagingConstants.Admission.DeterministicLane;
                    var phase = options.Value("phase") ?? PackagingConstants.Admission.ExecutionPhase;
                    var deadline = options.IntValue(
                        "deadline-seconds",
                        PackagingConstants.Admission.DefaultDeadlineSeconds);
                    var report = new AdmissionResultValidator().Validate(
                        cellId,
                        project,
                        filter,
                        lane,
                        phase,
                        deadline,
                        options.Value("reproduction") ?? $"rerun admission cell '{cellId}'",
                        new ProcessResult(
                            options.IntValue("exit-code", 0),
                            string.Empty,
                            string.Empty,
                            options.Has("timed-out"),
                            options.Has("killed-process-tree")),
                        options.RequiredValue("result"));
                    await WriteOutputAsync(options.Value("output"), ToJson(report), cancellationToken);
                    Console.WriteLine(
                        $"admission-results  {report.CellId} {report.Verdict}; " +
                        $"{report.Passed} passed, {report.Failed} failed, " +
                        $"{report.Skipped} skipped, {report.Unknown} unknown");
                    if (report.Verdict == PackagingConstants.Admission.PassedVerdict) return 0;
                    foreach (var reason in report.Reasons) Console.Error.WriteLine($"error  {reason}");
                    Console.Error.WriteLine($"reproduce  {report.ReproductionCommand}");
                    return 1;
                }
                case "quality":
                {
                    var packages = await repository.DiscoverPackagesAsync(cancellationToken);
                    var report = packageQualityCompiler.Compile(packages);
                    await WriteOutputAsync(
                        options.Value("output"),
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

                    Console.WriteLine(
                        $"quality    {report.Summary.Packages} package(s), " +
                        $"{report.Summary.RepairRequired} repair, " +
                        $"{report.Summary.ReviewRequired} review, " +
                        $"{report.Summary.StructurallyReady} structurally ready");
                    return 0;
                }
                case "product-surface":
                {
                    var packages = await repository.DiscoverPackagesAsync(cancellationToken);
                    var surface = await productSurfaceCompiler.CompileAsync(packages, cancellationToken);
                    var jsonContent = ProductSurfaceCompiler.ToJson(surface).TrimEnd();
                    var markdownContent = ProductSurfaceCompiler.ToMarkdown(surface).TrimEnd();
                    if (options.Has("check"))
                    {
                        if (options.Has("output") || options.Has("markdown"))
                        {
                            throw new InvalidOperationException(
                                "product-surface --check uses the canonical generated paths and cannot write outputs.");
                        }
                        generatedOutputVerifier.RequireMatch(
                            PackagingConstants.ProductSurface.GeneratedJsonPath,
                            jsonContent);
                        generatedOutputVerifier.RequireMatch(
                            PackagingConstants.ProductSurface.GeneratedMarkdownPath,
                            markdownContent);
                    }
                    else
                    {
                        await WriteOutputAsync(options.Value("output"), jsonContent, cancellationToken);
                        var markdown = options.Value("markdown");
                        if (markdown is not null)
                        {
                            await WriteOutputAsync(markdown, markdownContent, cancellationToken);
                        }
                    }

                    Console.WriteLine(
                        $"surface    {surface.Claims.Count} claim(s), {surface.Packages.Count} package(s)" +
                        (options.Has("check") ? ", generated outputs current" : string.Empty));
                    return 0;
                }
                case "api-baselines":
                {
                    var packages = await repository.DiscoverPackagesAsync(cancellationToken);
                    var surface = await productSurfaceCompiler.CompileAsync(packages, cancellationToken);
                    using var client = new HttpClient
                    {
                        BaseAddress = new Uri(PackagingConstants.PackageValidation.NuGetFlatContainerBaseUrl)
                    };
                    var validator = new PackageBaselineValidator(
                        (packageId, ct) => PackageBaselineValidator.ReadNuGetVersionsAsync(client, packageId, ct));
                    var report = await validator.ValidateAsync(packages, surface, cancellationToken);
                    Console.WriteLine(
                        $"api-baselines  {report.ConfiguredBaselines}/{report.AssemblyOwners} configured, " +
                        $"{report.FirstPublicationPending} first-publication pending, " +
                        $"{report.ContentOnlyOwners} content-only");
                    return 0;
                }
                case "inventory":
                {
                    var packages = await repository.DiscoverPackagesAsync(cancellationToken);
                    var json = JsonSerializer.Serialize(
                        packages,
                        new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
                    await WriteOutputAsync(options.Value("output"), json, cancellationToken);
                    Console.WriteLine($"inventory  {packages.Count} independently versioned package project(s)");
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

    private static void PrintHelp() => Console.WriteLine("""
        Koan package inventory

          inventory       [--output PATH]
          quality         [--output PATH] [--markdown PATH]
          product-surface [--output PATH] [--markdown PATH] [--check]
          api-baselines
          terminal-outcomes [--final] [--output PATH]
          native-admission --base SHA --candidate SHA [--configuration Debug|Release]
                          [--no-build] [--output PATH]
          admission       --id ID --project PATH --filter FILTER
                          [--lane deterministic|native] [--phase NAME]
                          [--deadline-seconds N] [--configuration Debug|Release]
                          [--no-build] [--output PATH]
          admission-results --id ID --project PATH --filter FILTER --result PATH
                          [--exit-code N] [--timed-out] [--killed-process-tree]
                          [--lane deterministic|native] [--phase NAME]
                          [--deadline-seconds N] [--reproduction COMMAND] [--output PATH]
        """);

    private static string ToJson<T>(T value) => JsonSerializer.Serialize(
        value,
        new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });

    private static async Task WriteOutputAsync(string? path, string content, CancellationToken cancellationToken)
    {
        if (path is null)
        {
            Console.WriteLine(content);
            return;
        }

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
            if (!argument.StartsWith("--", StringComparison.Ordinal))
                throw new InvalidOperationException($"Unexpected argument '{argument}'.");

            var name = argument[2..];
            string? value = null;
            if (index + 1 < input.Length && !input[index + 1].StartsWith("--", StringComparison.Ordinal))
                value = input[++index];
            result.values[name] = value;
        }

        return result;
    }

    public string? Value(string name) => values.GetValueOrDefault(name);
    public bool Has(string name) => values.ContainsKey(name);

    public string RequiredValue(string name) => Value(name) is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException($"--{name} requires a value.");

    public int IntValue(string name, int defaultValue)
    {
        var value = Value(name);
        if (value is null) return defaultValue;
        return int.TryParse(value, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"--{name} must be an integer.");
    }
}
