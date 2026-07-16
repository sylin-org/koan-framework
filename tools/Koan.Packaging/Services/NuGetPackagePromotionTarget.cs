using System.Security.Cryptography;
using Koan.Packaging.Infrastructure;

namespace Koan.Packaging.Services;

internal sealed class NuGetPackagePromotionTarget : IPackagePromotionTarget
{
    private const string PushTimeoutSeconds = "300";

    private readonly string repositoryRoot;
    private readonly string? apiKey;
    private readonly ProcessRunner processRunner;
    private readonly NuGetRegistry registry;

    public NuGetPackagePromotionTarget(
        string repositoryRoot,
        string? apiKey,
        ProcessRunner processRunner,
        NuGetRegistry registry)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            throw new ArgumentException("Repository root is required.", nameof(repositoryRoot));
        }
        this.repositoryRoot = repositoryRoot;
        this.apiKey = apiKey;
        this.processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public Task<bool> ExistsAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken) =>
        registry.ExistsAsync(packageId, version, cancellationToken);

    public Task PushPackageAsync(
        string packageId,
        string version,
        string packagePath,
        string expectedSha256,
        CancellationToken cancellationToken) =>
        PushWithRetryAsync(
            packageId,
            version,
            packagePath,
            expectedSha256,
            includeNoSymbols: true,
            cancellationToken);

    public Task ReplaySymbolsAsync(
        string packageId,
        string version,
        string symbolsPath,
        string expectedSha256,
        CancellationToken cancellationToken) =>
        PushWithRetryAsync(
            packageId,
            version,
            symbolsPath,
            expectedSha256,
            includeNoSymbols: false,
            cancellationToken);

    public async Task WaitUntilAvailableAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= PackagingConstants.RegistryAttempts; attempt++)
        {
            if (await registry.ExistsAsync(packageId, version, cancellationToken)) return;
            if (attempt < PackagingConstants.RegistryAttempts)
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(Math.Min(30, attempt * 2)),
                    cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Published package {packageId}/{version} did not become available before the registry timeout.");
    }

    internal static IReadOnlyList<string> BuildPackagePushArguments(
        string packagePath,
        string apiKey) =>
        BuildPushArguments(packagePath, apiKey, includeNoSymbols: true);

    internal static IReadOnlyList<string> BuildSymbolsPushArguments(
        string symbolsPath,
        string apiKey) =>
        BuildPushArguments(symbolsPath, apiKey, includeNoSymbols: false);

    internal static async Task VerifyArtifactHashAsync(
        string path,
        string expectedSha256,
        string identity,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Artifact for {identity} is missing: {path}");
        }
        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            throw new InvalidOperationException($"Prepared release wave has no SHA-256 for {identity}.");
        }

        await using var stream = File.OpenRead(path);
        var actualSha256 = Convert.ToHexString(
                await SHA256.HashDataAsync(stream, cancellationToken))
            .ToLowerInvariant();
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Artifact hash mismatch for {identity}: expected {expectedSha256}, found {actualSha256}.");
        }
    }

    private async Task PushWithRetryAsync(
        string packageId,
        string version,
        string artifactPath,
        string expectedSha256,
        bool includeNoSymbols,
        CancellationToken cancellationToken)
    {
        var identity = includeNoSymbols
            ? $"{packageId}/{version}"
            : $"{packageId}/{version} symbols";
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"NuGet publishing credential is required to publish {identity}.");
        }
        var arguments = includeNoSymbols
            ? BuildPackagePushArguments(artifactPath, apiKey)
            : BuildSymbolsPushArguments(artifactPath, apiKey);

        for (var attempt = 1; attempt <= PackagingConstants.PublishAttempts; attempt++)
        {
            // The escrow is authoritative only while the bytes still match its prepared hash.
            // Recheck before every subprocess attempt so a retry cannot publish mutated input.
            await VerifyArtifactHashAsync(
                artifactPath,
                expectedSha256,
                identity,
                cancellationToken);

            Console.WriteLine(includeNoSymbols
                ? $"push    {packageId}/{version}"
                : $"symbols {packageId}/{version}");
            var result = await processRunner.RunAsync(
                "dotnet",
                arguments,
                repositoryRoot,
                cancellationToken,
                echo: false);
            if (result.ExitCode == 0) return;
            if (attempt == PackagingConstants.PublishAttempts)
            {
                var diagnostics = SanitizeProcessDiagnostics(
                    string.Join(
                        Environment.NewLine,
                        new[] { result.StandardError, result.StandardOutput }
                            .Where(value => !string.IsNullOrWhiteSpace(value))
                            .Select(value => value.Trim())),
                    apiKey);
                throw new InvalidOperationException(
                    $"Failed to publish {identity} after {attempt} attempts." +
                    (diagnostics.Length == 0
                        ? string.Empty
                        : $"{Environment.NewLine}{diagnostics}"));
            }

            await Task.Delay(
                TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                cancellationToken);
        }
    }

    private static IReadOnlyList<string> BuildPushArguments(
        string artifactPath,
        string apiKey,
        bool includeNoSymbols)
    {
        if (string.IsNullOrWhiteSpace(artifactPath))
        {
            throw new ArgumentException("Artifact path is required.", nameof(artifactPath));
        }
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("NuGet publishing credential is required.", nameof(apiKey));
        }

        var arguments = new List<string>
        {
            "nuget",
            "push",
            artifactPath,
            "--source",
            PackagingConstants.NuGetSource,
            "--api-key",
            apiKey,
            "--skip-duplicate"
        };
        if (includeNoSymbols) arguments.Add("--no-symbols");
        arguments.Add("--timeout");
        arguments.Add(PushTimeoutSeconds);
        return arguments;
    }

    internal static string SanitizeProcessDiagnostics(
        string diagnostics,
        string apiKey)
    {
        if (string.IsNullOrWhiteSpace(diagnostics)) return string.Empty;
        if (string.IsNullOrEmpty(apiKey)) return diagnostics.Trim();
        return diagnostics
            .Replace(apiKey, "[redacted]", StringComparison.Ordinal)
            .Trim();
    }
}
