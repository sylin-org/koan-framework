using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;

namespace Koan.Packaging.Services;

internal sealed class TemplatePackageCompiler(string repositoryRoot)
{
    public async Task<PreparedTemplatePackage> PrepareAsync(
        IReadOnlyDictionary<string, string> versions,
        CancellationToken cancellationToken)
    {
        var root = Path.Combine(Path.GetTempPath(), $"koan-template-package-{Guid.NewGuid():N}");
        try
        {
            foreach (var directory in PackagingConstants.TemplatePackage.SourceDirectories)
            {
                await CopyDirectoryAsync(
                    Path.Combine(repositoryRoot, "templates", directory),
                    Path.Combine(root, directory),
                    cancellationToken);
            }

            await ReplaceAsync(
                Path.Combine(root, PackagingConstants.TemplatePackage.WebShortName, PackagingConstants.TemplatePackage.WebProjectFile),
                PackagingConstants.TemplatePackage.AppRangeToken,
                Range(versions, PackagingConstants.TemplatePackage.AppPackageId),
                cancellationToken);
            await ReplaceAsync(
                Path.Combine(root, PackagingConstants.TemplatePackage.WebShortName, PackagingConstants.TemplatePackage.WebProjectFile),
                PackagingConstants.TemplatePackage.SqliteRangeToken,
                Range(versions, PackagingConstants.TemplatePackage.SqlitePackageId),
                cancellationToken);
            await ReplaceAsync(
                Path.Combine(root, PackagingConstants.TemplatePackage.ConsoleShortName, PackagingConstants.TemplatePackage.ConsoleProjectFile),
                PackagingConstants.TemplatePackage.FoundationRangeToken,
                Range(versions, PackagingConstants.TemplatePackage.FoundationPackageId),
                cancellationToken);
            await ReplaceAsync(
                Path.Combine(root, PackagingConstants.TemplatePackage.ConsoleShortName, PackagingConstants.TemplatePackage.ConsoleProjectFile),
                PackagingConstants.TemplatePackage.SqliteRangeToken,
                Range(versions, PackagingConstants.TemplatePackage.SqlitePackageId),
                cancellationToken);

            var unresolved = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .FirstOrDefault(path => File.ReadAllText(path).Contains("__KOAN_", StringComparison.Ordinal));
            if (unresolved is not null)
            {
                throw new InvalidOperationException(
                    $"Prepared template '{Path.GetRelativePath(root, unresolved)}' contains an unresolved Koan package token.");
            }
            return new PreparedTemplatePackage(root);
        }
        catch
        {
            try { Directory.Delete(root, recursive: true); } catch { }
            throw;
        }
    }

    private static string Range(IReadOnlyDictionary<string, string> versions, string packageId) =>
        versions.TryGetValue(packageId, out var version)
            ? PackageCompatibility.FromVersion(version).Range
            : throw new InvalidOperationException(
                $"Template package compilation requires a selected or public version for '{packageId}'.");

    private static async Task ReplaceAsync(
        string path,
        string token,
        string value,
        CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(path, cancellationToken);
        var count = content.Split(token, StringSplitOptions.None).Length - 1;
        if (count != 1)
        {
            throw new InvalidOperationException(
                $"Template source '{Path.GetFileName(path)}' must contain token '{token}' exactly once; found {count}.");
        }
        await File.WriteAllTextAsync(path, content.Replace(token, value, StringComparison.Ordinal), cancellationToken);
    }

    private static async Task CopyDirectoryAsync(string source, string destination, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(source)) throw new InvalidOperationException($"Template source is missing: {source}");
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)
                     .Where(path => !IsBuildOutput(source, path)))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
                     .Where(path => !IsBuildOutput(source, path)))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = File.OpenRead(file);
            await using var output = File.Create(target);
            await input.CopyToAsync(output, cancellationToken);
        }
    }

    private static bool IsBuildOutput(string source, string path) =>
        Path.GetRelativePath(source, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                            segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
}
