using System.Runtime.CompilerServices;
using System.Text.Json;
using Koan.Packaging.Infrastructure;
using Xunit;

namespace Koan.Packaging.Tests;

public sealed class PackageTrainBaselinePolicyTests
{
    private const string AssemblyProject = "src/Koan.Core/Koan.Core.csproj";

    [Fact]
    public async Task AssemblyPackagesUseTheSharedTrainBaseline()
    {
        var properties = await EvaluateAsync("1.0.1");

        Assert.Equal("1.0.0", properties.GetProperty("KoanTrainBaselineVersion").GetString());
        Assert.Equal("1.0.0", properties.GetProperty("PackageValidationBaselineVersion").GetString());
        Assert.Equal("true", properties.GetProperty("EnablePackageValidation").GetString());
    }

    [Fact]
    public async Task ContentOnlyPackagesDoNotReceiveAnAssemblyBaseline()
    {
        var properties = await EvaluateAsync("1.0.1", "templates/Sylin.Koan.Templates.csproj");

        Assert.Equal("1.0.0", properties.GetProperty("KoanTrainBaselineVersion").GetString());
        Assert.Equal("", properties.GetProperty("PackageValidationBaselineVersion").GetString());
    }

    private static async Task<JsonElement> EvaluateAsync(
        string packageVersion,
        string projectPath = AssemblyProject)
    {
        var output = await new ProcessRunner().RequireAsync(
            "dotnet",
            Arguments(packageVersion, readProperties: true, projectPath: projectPath),
            FindKoanRoot(),
            CancellationToken.None);
        using var document = JsonDocument.Parse(output);
        return document.RootElement.GetProperty("Properties").Clone();
    }

    private static IReadOnlyList<string> Arguments(
        string packageVersion,
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
        if (readProperties)
        {
            arguments.Add("-getProperty:KoanTrainBaselineVersion,PackageValidationBaselineVersion,EnablePackageValidation");
        }
        return arguments;
    }

    private static string FindKoanRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
