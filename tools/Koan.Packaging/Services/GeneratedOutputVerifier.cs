namespace Koan.Packaging.Services;

internal sealed class GeneratedOutputVerifier(string repositoryRoot)
{
    public void RequireMatch(string repositoryPath, string generatedContent)
    {
        var fullPath = Path.GetFullPath(Path.Combine(repositoryRoot, repositoryPath));
        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException(
                $"Generated product output '{repositoryPath}' is missing. Run: " +
                "dotnet run --project tools/Koan.Packaging -- product-surface " +
                "--output docs/reference/product-surface.json --markdown docs/reference/product-surface.md");
        }

        var expected = generatedContent.TrimEnd() + Environment.NewLine;
        var actual = File.ReadAllText(fullPath);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Generated product output '{repositoryPath}' is stale. Run: " +
                "dotnet run --project tools/Koan.Packaging -- product-surface " +
                "--output docs/reference/product-surface.json --markdown docs/reference/product-surface.md");
        }
    }
}
