using Koan.Cache.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Koan.Tests.Cache.Topology.Specs;

public sealed class CacheTraceFilterSpec
{
    /// <summary>Records every log invocation for later assertion.</summary>
    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel level, string message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    [Fact]
    public void ShouldTrace_returns_true_only_for_exact_configured_key()
    {
        var prev = CacheTraceFilter.OverrideForTesting("Todo:_:abc-123");
        try
        {
            CacheTraceFilter.ShouldTrace("Todo:_:abc-123").Should().BeTrue();
            CacheTraceFilter.ShouldTrace("Todo:_:abc-124").Should().BeFalse();
            CacheTraceFilter.ShouldTrace("Other:_:abc-123").Should().BeFalse();
        }
        finally
        {
            CacheTraceFilter.OverrideForTesting(prev);
        }
    }

    [Fact]
    public void ShouldTrace_is_case_sensitive()
    {
        var prev = CacheTraceFilter.OverrideForTesting("Todo:_:ABC");
        try
        {
            CacheTraceFilter.ShouldTrace("Todo:_:abc").Should().BeFalse();
            CacheTraceFilter.ShouldTrace("Todo:_:ABC").Should().BeTrue();
        }
        finally
        {
            CacheTraceFilter.OverrideForTesting(prev);
        }
    }

    [Fact]
    public void ShouldTrace_with_no_configuration_always_false()
    {
        var prev = CacheTraceFilter.OverrideForTesting(null);
        try
        {
            CacheTraceFilter.ShouldTrace("anything").Should().BeFalse();
        }
        finally
        {
            CacheTraceFilter.OverrideForTesting(prev);
        }
    }

    [Fact]
    public void LogIfTraced_emits_Information_only_on_match()
    {
        var prev = CacheTraceFilter.OverrideForTesting("hot-key");
        try
        {
            var logger = new CapturingLogger();

            CacheTraceFilter.LogIfTraced(logger, "hot-key", "fetch", outcome: "hit");
            CacheTraceFilter.LogIfTraced(logger, "cold-key", "fetch", outcome: "hit");

            logger.Entries.Should().ContainSingle();
            logger.Entries[0].level.Should().Be(LogLevel.Information);
            logger.Entries[0].message.Should().Contain("hot-key").And.Contain("fetch").And.Contain("hit");
        }
        finally
        {
            CacheTraceFilter.OverrideForTesting(prev);
        }
    }

    [Fact]
    public void Empty_or_null_key_never_traces()
    {
        var prev = CacheTraceFilter.OverrideForTesting("anything");
        try
        {
            CacheTraceFilter.ShouldTrace("").Should().BeFalse();
            CacheTraceFilter.ShouldTrace(null!).Should().BeFalse();
        }
        finally
        {
            CacheTraceFilter.OverrideForTesting(prev);
        }
    }
}
