using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Koan.Core.Logging;
using Koan.Data.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Configuration;

/// <summary>
/// (X-f2-failure-coverage) Failure-path coverage for the F2-data-core burn-down (430a05e8) in
/// <see cref="AdapterConnectionResolver.GetSourceSetting{T}"/>: a malformed adapter setting must DEGRADE —
/// warn (no longer silently swallow) and fall through to the next priority, ultimately the supplied
/// default — never throw, never return a wrong/zero value.
///
/// Two halves:
/// 1. Behavioral (seam-free): a malformed value at each origin (source-definition / config-source /
///    config-adapter) returns the supplied default; a well-formed value still coerces (positive control).
/// 2. Observability — the ConfigWarning deferred from F2-sqlite's test pass. It routes through a
///    static <see cref="KoanLog.KoanLogScope"/> that follows the current host. This focused unit spec
///    intentionally has no host, so it observes the single <c>KoanLog.Write</c> chokepoint through the
///    internal <c>KoanLog.TestSink</c> seam, filtered to a unique provider so no concurrent emission can
///    pollute the assertion.
/// </summary>
public sealed class AdapterConnectionResolverFailurePathSpec
{
    private const string Provider = "sqlite";
    private const string Setting = "MaxPageSize";
    private const int Default = 50;
    private const string Malformed = "notanumber";

    [Fact]
    public void Malformed_source_definition_setting_falls_through_to_default()
    {
        var registry = new DataSourceRegistry();
        registry.RegisterSource(new DataSourceRegistry.SourceDefinition(
            "Default", Provider, "", new Dictionary<string, string> { [Setting] = Malformed }));

        var value = AdapterConnectionResolver.GetSourceSetting(
            new ConfigurationBuilder().Build(), registry, Provider, "Default", Setting, Default);

        value.Should().Be(Default);
    }

    [Fact]
    public void Malformed_config_source_setting_falls_through_to_default()
    {
        var registry = new DataSourceRegistry();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Koan:Data:Sources:Default:{Provider}:{Setting}"] = Malformed
            })
            .Build();

        var value = AdapterConnectionResolver.GetSourceSetting(config, registry, Provider, "Default", Setting, Default);

        value.Should().Be(Default);
    }

    [Fact]
    public void Malformed_config_adapter_setting_falls_through_to_default()
    {
        var registry = new DataSourceRegistry();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Koan:Data:{Provider}:{Setting}"] = Malformed
            })
            .Build();

        var value = AdapterConnectionResolver.GetSourceSetting(config, registry, Provider, "Default", Setting, Default);

        value.Should().Be(Default);
    }

    [Fact]
    public void Wellformed_setting_is_coerced_not_defaulted()
    {
        // Positive control: the warn/fall-through path must NOT trip for a valid value.
        var registry = new DataSourceRegistry();
        registry.RegisterSource(new DataSourceRegistry.SourceDefinition(
            "Default", Provider, "", new Dictionary<string, string> { [Setting] = "100" }));

        var value = AdapterConnectionResolver.GetSourceSetting(
            new ConfigurationBuilder().Build(), registry, Provider, "Default", Setting, Default);

        value.Should().Be(100);
    }

    [Fact]
    public void Malformed_setting_emits_one_config_warning_with_provenance()
    {
        // Unique provider so any unrelated concurrent KoanLog.Write cannot pollute the capture.
        const string probeProvider = "x-f2-probe-adapter";
        var captured = new List<(KoanLogStage Stage, LogLevel Level, string? Outcome, (string Key, object? Value)[] Context)>();

        KoanLog.TestSink = (stage, level, action, outcome, context) =>
        {
            if (action != "adapter-setting.coerce") return;
            if (!context.Any(kv => kv.Key == "provider" && string.Equals(kv.Value?.ToString(), probeProvider))) return;
            lock (captured) captured.Add((stage, level, outcome, context));
        };
        try
        {
            var registry = new DataSourceRegistry();
            registry.RegisterSource(new DataSourceRegistry.SourceDefinition(
                "Default", probeProvider, "", new Dictionary<string, string> { [Setting] = Malformed }));

            var value = AdapterConnectionResolver.GetSourceSetting(
                new ConfigurationBuilder().Build(), registry, probeProvider, "Default", Setting, Default);

            value.Should().Be(Default);
        }
        finally
        {
            KoanLog.TestSink = null;
        }

        List<(KoanLogStage Stage, LogLevel Level, string? Outcome, (string Key, object? Value)[] Context)> snapshot;
        lock (captured) snapshot = captured.ToList();

        // Exactly one warning for the one malformed value (no silent swallow, no duplicate).
        snapshot.Should().ContainSingle();
        var entry = snapshot[0];
        entry.Stage.Should().Be(KoanLogStage.Cnfg);
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Outcome.Should().Be("malformed-value");

        // Provenance an operator needs to find the typo'd setting.
        var ctx = entry.Context.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString());
        ctx.Should().Contain("provider", probeProvider);
        ctx.Should().Contain("setting", Setting);
        ctx.Should().Contain("origin", "source-definition");
        ctx.Should().ContainKey("reason");
    }
}
