using System.Runtime.CompilerServices;
using System.Text.Json;
using Koan.Packaging.Infrastructure;
using Xunit;

namespace Koan.Packaging.Tests;

public sealed class PackageTrainBaselinePolicyTests
{
    private const string AssemblyProject = "src/Koan.Core/Koan.Core.csproj";

    [Fact]
    public async Task BootstrapReleaseKeepsTheHistoricalPackageBaseline()
    {
        var properties = await EvaluateAsync("1.0.0");

        Assert.Equal("", properties.GetProperty("KoanTrainBaselineVersion").GetString());
        Assert.Equal("0.20.4", properties.GetProperty("PackageValidationBaselineVersion").GetString());
        Assert.Equal("true", properties.GetProperty("EnablePackageValidation").GetString());
    }

    [Theory]
    [InlineData(AssemblyProject)]
    [InlineData("templates/Sylin.Koan.Templates.csproj")]
    public async Task LaterPublicReleaseFailsWithoutTheCentralTrainBaseline(string projectPath)
    {
        var result = await new ProcessRunner().RunAsync(
            "dotnet",
            Arguments("1.0.1", projectPath: projectPath),
            FindKoanRoot(),
            CancellationToken.None);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("requires KoanTrainBaselineVersion", result.StandardError + result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaterPublicReleaseUsesTheCentralTrainBaseline()
    {
        var properties = await EvaluateAsync("1.0.1", "1.0.0");

        Assert.Equal("1.0.0", properties.GetProperty("KoanTrainBaselineVersion").GetString());
        Assert.Equal("1.0.0", properties.GetProperty("PackageValidationBaselineVersion").GetString());
        Assert.Equal("true", properties.GetProperty("EnablePackageValidation").GetString());
    }

    private static async Task<JsonElement> EvaluateAsync(string packageVersion, string? trainBaseline = null)
    {
        var output = await new ProcessRunner().RequireAsync(
            "dotnet",
            Arguments(packageVersion, trainBaseline, readProperties: true),
            FindKoanRoot(),
            CancellationToken.None);
        using var document = JsonDocument.Parse(output);
        return document.RootElement.GetProperty("Properties").Clone();
    }

    private static IReadOnlyList<string> Arguments(
        string packageVersion,
        string? trainBaseline = null,
        bool readProperties = false,
        string projectPath = AssemblyProject)
    {
        var arguments = new List<string>
        {
            "msbuild",
            projectPath,
            "-nologo",
            "-target:ValidateKoanPackageTrainBaseline",
            "-property:PublicRelease=true",
            $"-property:PackageVersion={packageVersion}"
        };
        if (trainBaseline is not null)
        {
            arguments.Add($"-property:KoanTrainBaselineVersion={trainBaseline}");
        }
        if (readProperties)
        {
            arguments.Add("-getProperty:KoanTrainBaselineVersion,PackageValidationBaselineVersion,EnablePackageValidation");
        }
        return arguments;
    }

    private static string FindKoanRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
