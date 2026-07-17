using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace Koan.Packaging.Tests;

public sealed partial class ConnectorTelemetryPolicyTests
{
    [Fact]
    public void Connector_startup_configuration_discovery_and_health_sources_use_the_safe_Koan_log_boundary()
    {
        var connectorRoot = Path.Combine(RepositoryRoot(), "src", "Connectors");
        var offenders = Directory
            .EnumerateFiles(connectorRoot, "*.cs", SearchOption.AllDirectories)
            .Where(IsBoundedTelemetrySource)
            .Where(path => DirectLoggerCall().IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(RepositoryRoot(), path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Connector startup, configuration, discovery, orchestration, and health telemetry must cross KoanLog " +
            "so credential-shaped context is de-identified once. Direct ILogger calls remain in: " +
            string.Join(", ", offenders));
    }

    private static bool IsBoundedTelemetrySource(string path)
    {
        var normalized = path.Replace('\\', '/');
        var fileName = Path.GetFileName(path);
        return normalized.Contains("/Discovery/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/Initialization/", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("OptionsConfigurator.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("OrchestrationEvaluator.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("HealthContributor.cs", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"\.\s*Log(?:Trace|Debug|Information|Warning|Error|Critical)\s*\(")]
    private static partial Regex DirectLoggerCall();

    private static string RepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
