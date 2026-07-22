using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;

namespace Koan.Packaging.Services;

internal sealed class AdmissionRunner(
    string repositoryRoot,
    ProcessRunner processRunner,
    AdmissionResultValidator resultValidator)
{
    public async Task<AdmissionReport> RunAsync(
        string cellId,
        string project,
        string filter,
        string lane,
        string phase,
        int deadlineSeconds,
        string configuration,
        bool noBuild,
        CancellationToken cancellationToken)
    {
        RequireValue(cellId, "id");
        RequireValue(project, "project");
        RequireValue(filter, "filter");
        RequireValue(phase, "phase");
        if (lane is not (PackagingConstants.Admission.DeterministicLane or PackagingConstants.Admission.NativeLane))
            throw new InvalidOperationException("admission --lane must be 'deterministic' or 'native'.");
        if (deadlineSeconds is < PackagingConstants.Admission.MinimumDeadlineSeconds
            or > PackagingConstants.Admission.MaximumDeadlineSeconds)
        {
            throw new InvalidOperationException(
                $"admission --deadline-seconds must be between {PackagingConstants.Admission.MinimumDeadlineSeconds} " +
                $"and {PackagingConstants.Admission.MaximumDeadlineSeconds}.");
        }
        if (configuration is not ("Debug" or "Release"))
            throw new InvalidOperationException("admission --configuration must be 'Debug' or 'Release'.");

        var projectPath = Path.IsPathRooted(project)
            ? Path.GetFullPath(project)
            : Path.GetFullPath(project, repositoryRoot);
        var relativeProject = Path.GetRelativePath(repositoryRoot, projectPath).Replace('\\', '/');
        if (relativeProject == ".." || relativeProject.StartsWith("../", StringComparison.Ordinal))
            throw new InvalidOperationException("admission --project must be inside the repository.");
        if (!File.Exists(projectPath))
            throw new InvalidOperationException($"admission project does not exist: {relativeProject}");

        var resultsRoot = Path.Combine(Path.GetTempPath(), $"koan-admission-{Guid.NewGuid():N}");
        Directory.CreateDirectory(resultsRoot);
        var resultPath = Path.Combine(resultsRoot, $"result{PackagingConstants.Admission.TrxExtension}");
        var arguments = new List<string>
        {
            "test",
            projectPath,
            "--configuration",
            configuration,
            "--filter",
            filter,
            "--logger",
            "trx;LogFileName=result.trx",
            "--results-directory",
            resultsRoot,
            "--nologo"
        };
        if (noBuild) arguments.Add("--no-build");

        var reproductionArguments = new List<string>
        {
            "run", "--project", "tools/Koan.Packaging/Koan.Packaging.csproj", "--", "admission",
            "--id", cellId,
            "--project", relativeProject,
            "--filter", filter,
            "--lane", lane,
            "--phase", phase,
            "--deadline-seconds", deadlineSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--configuration", configuration
        };
        if (noBuild) reproductionArguments.Add("--no-build");
        var reproduction = "dotnet " + string.Join(" ", reproductionArguments.Select(Quote));
        try
        {
            var process = await processRunner.RunAsync(
                "dotnet",
                arguments,
                repositoryRoot,
                cancellationToken,
                timeout: TimeSpan.FromSeconds(deadlineSeconds));
            return resultValidator.Validate(
                cellId,
                relativeProject,
                filter,
                lane,
                phase,
                deadlineSeconds,
                reproduction,
                process,
                resultPath);
        }
        finally
        {
            if (Directory.Exists(resultsRoot)) Directory.Delete(resultsRoot, recursive: true);
        }
    }

    private static void RequireValue(string value, string option)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"admission --{option} requires a value.");
    }

    private static string Quote(string value) => value.Any(char.IsWhiteSpace)
        ? $"\"{value.Replace("\"", "\\\"")}\""
        : value;
}
