using System.Runtime.CompilerServices;
using System.Text.Json;
using Koan.Packaging.Infrastructure;
using Xunit;

namespace Koan.Packaging.Tests;

public sealed class PackageTrainBaselinePolicyTests
{
    private const string AssemblyProject = "src/Koan.Core/Koan.Core.csproj";

    /// <summary>
    /// Assembly packages do not validate against a baseline yet, and that is deliberate.
    ///
    /// <para>1.0.0 is on nuget.org but the framework is not announced, so nothing is built on it. Validating
    /// every assembly against it reported 101 differences from one stabilization cycle — the schema
    /// orchestrator becoming a single owner, the vector adapter surface settling — all of them intended, none
    /// of them owed to anyone. Koan 1.x is the stabilization line and the surface is still being cut down.</para>
    ///
    /// <para>At announcement, flip <c>KoanHasPublishedBaseline</c> back on, set
    /// <c>KoanTrainBaselineVersion</c> to whatever is published then, and invert this test with it. The central
    /// baseline version stays asserted either way, because the switch is about whether it is enforced, not
    /// about whether the train has one.</para>
    /// </summary>
    [Fact]
    public async Task AssemblyPackagesDoNotValidateAgainstABaselineBeforeAnnouncement()
    {
        var properties = await EvaluateAsync("1.0.1");

        Assert.Equal("1.0.0", properties.GetProperty("KoanTrainBaselineVersion").GetString());
        Assert.Equal("", properties.GetProperty("PackageValidationBaselineVersion").GetString());
        Assert.Equal("", properties.GetProperty("EnablePackageValidation").GetString());
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
