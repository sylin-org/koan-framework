using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Koan.Packaging.Tests;

public sealed class DependencyGovernanceTests
{
    private static readonly string[] ReleaseRoots =
    [
        "src",
        "tests",
        "samples",
        "templates",
        "packaging",
        "tools"
    ];

    [Fact]
    public void ReleaseRelevantProjectsUseOneCentralExternalPackageConstitution()
    {
        var root = FindKoanRoot();
        var centralPath = Path.Combine(root, "Directory.Packages.props");
        var central = XDocument.Load(centralPath);
        Assert.Equal("true", central.Descendants("ManagePackageVersionsCentrally").First().Value);

        var versions = central.Descendants("PackageVersion")
            .Select(element => new
            {
                Id = (string?)element.Attribute("Include"),
                Version = (string?)element.Attribute("Version")
            })
            .ToArray();
        Assert.All(versions, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Id));
            Assert.Matches(@"^\d+\.\d+(?:\.\d+){0,2}$", entry.Version ?? string.Empty);
        });
        Assert.DoesNotContain(
            versions.GroupBy(entry => entry.Id!, StringComparer.OrdinalIgnoreCase),
            group => group.Count() > 1);

        var centralIds = versions.Select(entry => entry.Id!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var manifest in EnumerateReleaseRelevantManifests(root))
        {
            var document = XDocument.Load(manifest);
            foreach (var reference in document.Descendants("PackageReference"))
            {
                var id = (string?)reference.Attribute("Include");
                if (string.IsNullOrWhiteSpace(id) || id.StartsWith("Sylin.Koan", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var relative = Path.GetRelativePath(root, manifest);
                Assert.Null(reference.Attribute("Version"));
                Assert.Null(reference.Attribute("VersionOverride"));
                Assert.True(centralIds.Contains(id), $"External package '{id}' in '{relative}' is absent from Directory.Packages.props.");
            }
        }
    }

    [Fact]
    public void SdkAndGitHubActionsAreExactAndCentrallySelected()
    {
        var root = FindKoanRoot();
        using var globalJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "global.json")));
        var sdkVersion = globalJson.RootElement.GetProperty("sdk").GetProperty("version").GetString();
        Assert.Matches(@"^\d+\.\d+\.\d+$", sdkVersion ?? string.Empty);
        Assert.Equal("disable", globalJson.RootElement.GetProperty("sdk").GetProperty("rollForward").GetString());

        var workflows = Directory.EnumerateFiles(Path.Combine(root, ".github", "workflows"), "*.yml")
            .SelectMany(File.ReadLines)
            .Where(line => line.Contains("uses: actions/", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(workflows);
        Assert.All(workflows, line => Assert.Matches(@"uses: actions/[a-z-]+@[0-9a-f]{40}(?:\s+#\s+v\d+\.\d+\.\d+)?$", line.Trim()));
    }

    [Fact]
    public void ReleaseRelevantContainerImagesUseExactNonFloatingTags()
    {
        var root = FindKoanRoot();
        var manifests = new[]
            {
                Path.Combine(root, ".Koan"),
                Path.Combine(root, "samples")
            }
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            .Where(path => Path.GetFileName(path).StartsWith("Dockerfile", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
            .Where(path => !IsFrozenOrGenerated(root, path));

        var imageReferences = manifests
            .SelectMany(path => File.ReadLines(path).Select(line => (path, line: line.Trim())))
            .Select(entry => entry.line.StartsWith("image:", StringComparison.OrdinalIgnoreCase)
                ? (entry.path, image: entry.line["image:".Length..].Trim().Trim('"', '\''))
                : entry.line.StartsWith("FROM ", StringComparison.OrdinalIgnoreCase)
                    ? (entry.path, image: entry.line["FROM ".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0])
                    : (entry.path, image: string.Empty))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.image) &&
                            !entry.image.StartsWith("koan-", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.NotEmpty(imageReferences);
        Assert.All(imageReferences, entry =>
        {
            var relative = Path.GetRelativePath(root, entry.path);
            var tagSeparator = entry.image.LastIndexOf(':');
            Assert.True(tagSeparator > entry.image.LastIndexOf('/'), $"Container image '{entry.image}' in '{relative}' has no tag.");
            var tag = entry.image[(tagSeparator + 1)..];
            Assert.DoesNotMatch(@"^(latest|stable|main|master|dev|edge|\d+)$", tag);
        });

        var productionSources = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories);
        var floatingDefaults = productionSources
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (path, line, number: index + 1)))
            .Where(entry => Regex.IsMatch(entry.line, @"DefaultTag\s*=\s*""(?:latest|stable|main|master|dev|edge|\d+)"""))
            .ToArray();
        Assert.Empty(floatingDefaults);
    }

    private static IEnumerable<string> EnumerateReleaseRelevantManifests(string root)
    {
        foreach (var file in new[] { "Directory.Build.props", "Directory.Build.targets" })
        {
            var path = Path.Combine(root, file);
            if (File.Exists(path)) yield return path;
        }

        foreach (var releaseRoot in ReleaseRoots)
        {
            var directory = Path.Combine(root, releaseRoot);
            if (!Directory.Exists(directory)) continue;

            foreach (var path in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
                         .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                                        path.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
                                        path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase))
                         .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                             .Any(segment => segment is "bin" or "obj"))
                         .Where(path => !Path.GetRelativePath(root, path).Replace('\\', '/')
                             .StartsWith("samples/archive/", StringComparison.OrdinalIgnoreCase)))
            {
                yield return path;
            }
        }
    }

    private static bool IsFrozenOrGenerated(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        return relative.StartsWith("samples/archive/", StringComparison.OrdinalIgnoreCase) ||
               relative.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
               relative.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
               relative.Contains("/.koan/", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindKoanRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
