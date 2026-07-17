using System.Net.Http.Json;
using System.Text.Json;
using Koan.Packaging.Infrastructure;

namespace Koan.Packaging.Services;

internal sealed class TemplatePackageProbe(ProcessRunner processRunner)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CleanRoomApplicationCompiler compiler = new(processRunner);

    public async Task VerifyAsync(
        string templatePackage,
        string cleanRoomRoot,
        string nugetConfig,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(templatePackage))
        {
            throw new InvalidOperationException($"Template package is missing: {templatePackage}");
        }

        var cliHome = Path.Combine(cleanRoomRoot, "dotnet-home");
        var applications = Path.Combine(cleanRoomRoot, "templates");
        var web = Path.Combine(applications, "web");
        var console = Path.Combine(applications, "console");
        Directory.CreateDirectory(cliHome);
        Directory.CreateDirectory(applications);
        IReadOnlyDictionary<string, string?> environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [PackagingConstants.TemplatePackage.DotNetCliHomeEnvironmentVariable] = cliHome,
            [PackagingConstants.TemplatePackage.DotNetSkipFirstTimeExperienceEnvironmentVariable] = "1",
            [PackagingConstants.TemplatePackage.DotNetTelemetryOptOutEnvironmentVariable] = "1"
        };

        await processRunner.RequireAsync(
            "dotnet",
            ["new", "install", Path.GetFullPath(templatePackage)],
            applications,
            cancellationToken,
            echo: true,
            environment: environment);
        var webProject = await CreateAsync(
            PackagingConstants.TemplatePackage.WebShortName,
            web,
            applications,
            environment,
            cancellationToken);
        var consoleProject = await CreateAsync(
            PackagingConstants.TemplatePackage.ConsoleShortName,
            console,
            applications,
            environment,
            cancellationToken);

        await compiler.RestoreAndBuildAsync(
            web,
            webProject,
            nugetConfig,
            packageProperties: null,
            environment: environment,
            cancellationToken: cancellationToken);
        await compiler.RestoreAndBuildAsync(
            console,
            consoleProject,
            nugetConfig,
            packageProperties: null,
            environment: environment,
            cancellationToken: cancellationToken);

        await VerifyConsoleAsync(console, consoleProject, environment, cancellationToken);
        await VerifyWebAsync(web, webProject, cancellationToken);
    }

    private async Task<string> CreateAsync(
        string shortName,
        string outputDirectory,
        string workingDirectory,
        IReadOnlyDictionary<string, string?> environment,
        CancellationToken cancellationToken)
    {
        await processRunner.RequireAsync(
            "dotnet",
            ["new", shortName, "-o", outputDirectory],
            workingDirectory,
            cancellationToken,
            echo: true,
            environment: environment);
        return RequireProjectFile(outputDirectory, shortName);
    }

    internal static string RequireProjectFile(string outputDirectory, string shortName)
    {
        var projects = Directory.EnumerateFiles(outputDirectory, "*.csproj", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .ToArray();
        return projects.Length == 1
            ? projects[0]!
            : throw new InvalidOperationException(
                $"Generated template '{shortName}' produced {projects.Length} project files; expected exactly one.");
    }

    private async Task VerifyConsoleAsync(
        string applicationDirectory,
        string projectFile,
        IReadOnlyDictionary<string, string?> environment,
        CancellationToken cancellationToken)
    {
        var output = await processRunner.RequireAsync(
            "dotnet",
            ["run", "--project", projectFile, "-c", "Release", "--no-build"],
            applicationDirectory,
            cancellationToken,
            echo: true,
            environment: environment);
        foreach (var expected in new[]
                 {
                     PackagingConstants.TemplatePackage.ConsoleLoadedResult,
                     PackagingConstants.TemplatePackage.ConsoleQueryResult
                 })
        {
            if (output.Contains(expected, StringComparison.OrdinalIgnoreCase)) continue;
            throw new InvalidOperationException(
                $"Generated {PackagingConstants.TemplatePackage.ConsoleShortName} did not report business result '{expected}'.");
        }
    }

    private static async Task VerifyWebAsync(
        string applicationDirectory,
        string projectFile,
        CancellationToken cancellationToken)
    {
        await using var host = ApplicationProbeHost.Start(
            applicationDirectory,
            projectFile,
            "template-web",
            configureIsolatedSqliteTarget: false);
        try
        {
            await host.WaitUntilReadyAsync(cancellationToken);
            using var response = await host.Http.PostAsJsonAsync(
                PackagingConstants.TemplatePackage.TodosPath,
                new { title = PackagingConstants.TemplatePackage.TodoTitle },
                JsonOptions,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            if (!root.TryGetProperty(PackagingConstants.TemplatePackage.TodoIdProperty, out var id) ||
                string.IsNullOrWhiteSpace(id.GetString()) ||
                !root.TryGetProperty(PackagingConstants.TemplatePackage.TodoTitleProperty, out var title) ||
                !string.Equals(title.GetString(), PackagingConstants.TemplatePackage.TodoTitle, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Generated {PackagingConstants.TemplatePackage.WebShortName} did not persist and return its Todo business result.");
            }
        }
        catch (Exception exception)
        {
            throw await host.FailureAsync("Generated web template proof failed", exception);
        }
    }
}
