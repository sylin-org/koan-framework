using Koan.Packaging.Infrastructure;

namespace Koan.Packaging.Services;

internal sealed class CleanRoomApplicationCompiler(ProcessRunner processRunner)
{
    public async Task RestoreAndBuildAsync(
        string applicationDirectory,
        string projectFile,
        string nugetConfig,
        IReadOnlyCollection<string>? packageProperties,
        IReadOnlyDictionary<string, string?>? environment,
        CancellationToken cancellationToken)
    {
        packageProperties ??= [];
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
            echo: true,
            environment: environment);
        await processRunner.RequireAsync(
            "dotnet",
            new[] { "build", projectFile, "-c", "Release", "--no-restore", "--nologo" }
                .Concat(packageProperties),
            applicationDirectory,
            cancellationToken,
            echo: true,
            environment: environment);
    }
}
