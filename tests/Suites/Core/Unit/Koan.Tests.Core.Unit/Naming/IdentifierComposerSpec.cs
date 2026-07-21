using System;
using System.Text;
using AwesomeAssertions;
using Koan.Core.Naming;
using Xunit;

namespace Koan.Tests.Core.Unit.Naming;

/// <summary>
/// ARCH-0096: the identifier-composition engine — anchor + ordered policy-rendered particles. Pins the
/// deterministic ordering, the omission rules, the per-consumer policy (storage vs cache), the byte-limit
/// clamp, and the allocation-free fast paths.
/// </summary>
public class IdentifierComposerSpec
{
    private sealed class OmitEmptyFormatter : IParticleFormatter
    {
        public string? Format(string? v) => string.IsNullOrEmpty(v) ? null : v;
    }

    private sealed class SentinelFormatter : IParticleFormatter
    {
        public string? Format(string? v) => string.IsNullOrEmpty(v) ? "_" : v;
    }

    // Storage-style: '#' separator, omit empty particles, optional adapter byte limit.
    private static CompositionPolicy Storage(int? max = null) => new("#", new OmitEmptyFormatter(), max);

    // Cache-style: ':' separator, empty rendered as the "_" sentinel, no limit.
    private static CompositionPolicy Cache() => new(":", new SentinelFormatter());

    [Fact]
    public void No_particles_returns_the_anchor_reference()
    {
        var anchor = "Widget";
        IdentifierComposer.Compose(anchor, ReadOnlySpan<Particle>.Empty, Storage()).Should().BeSameAs(anchor);
    }

    [Fact]
    public void Single_particle_appends_token_after_separator()
    {
        var p = new[] { new Particle(0, "partition", "alpha") };
        IdentifierComposer.Compose("Widget", p, Storage()).Should().Be("Widget#alpha");
    }

    [Fact]
    public void Single_omitted_particle_returns_the_anchor_reference()
    {
        var anchor = "Widget";
        var p = new[] { new Particle(0, "partition", "") };
        IdentifierComposer.Compose(anchor, p, Storage()).Should().BeSameAs(anchor);
    }

    [Fact]
    public void Multi_particle_joins_in_order()
    {
        var p = new[] { new Particle(0, "partition", "alpha"), new Particle(1, "tenant", "acme") };
        IdentifierComposer.Compose("Widget", p, Storage()).Should().Be("Widget#alpha#acme");
    }

    [Fact]
    public void Multi_particle_order_is_deterministic_regardless_of_input_order()
    {
        var ascending = new[] { new Particle(0, "a", "1"), new Particle(1, "b", "2"), new Particle(2, "c", "3") };
        var scrambled = new[] { new Particle(2, "c", "3"), new Particle(0, "a", "1"), new Particle(1, "b", "2") };
        var a = IdentifierComposer.Compose("X", ascending, Storage());
        IdentifierComposer.Compose("X", scrambled, Storage()).Should().Be(a);
        a.Should().Be("X#1#2#3");
    }

    [Fact]
    public void Order_ties_break_by_axis_ordinal()
    {
        var p = new[] { new Particle(0, "zebra", "z"), new Particle(0, "alpha", "a") };
        IdentifierComposer.Compose("X", p, Storage()).Should().Be("X#a#z");
    }

    [Fact]
    public void Omitted_particles_are_skipped_in_the_multi_path()
    {
        var p = new[] { new Particle(0, "partition", "alpha"), new Particle(1, "tenant", "") };
        IdentifierComposer.Compose("Widget", p, Storage()).Should().Be("Widget#alpha");
    }

    [Fact]
    public void All_omitted_multi_particle_returns_the_anchor_reference()
    {
        var anchor = "Widget";
        var p = new[] { new Particle(0, "a", ""), new Particle(1, "b", "") };
        IdentifierComposer.Compose(anchor, p, Storage()).Should().BeSameAs(anchor);
    }

    [Fact]
    public void Cache_policy_renders_empty_as_the_sentinel()
    {
        var p = new[] { new Particle(0, "partition", ""), new Particle(1, "id", "42") };
        IdentifierComposer.Compose("Widget", p, Cache()).Should().Be("Widget:_:42");
    }

    [Fact]
    public void Over_limit_identifier_is_clamped_to_prefix_plus_hash()
    {
        var anchor = new string('w', 100);
        var p = new[] { new Particle(0, "partition", new string('p', 100)) };
        var result = IdentifierComposer.Compose(anchor, p, Storage(max: 63));
        Encoding.UTF8.GetByteCount(result).Should().BeLessThanOrEqualTo(63);
        result.Should().Contain("#");
    }

    [Fact]
    public void Clamp_is_deterministic()
    {
        var anchor = new string('w', 100);
        var p = new[] { new Particle(0, "partition", "alpha") };
        var a = IdentifierComposer.Compose(anchor, p, Storage(max: 40));
        IdentifierComposer.Compose(anchor, p, Storage(max: 40)).Should().Be(a);
    }

    [Fact]
    public void Within_limit_identifier_is_unchanged()
    {
        var p = new[] { new Particle(0, "partition", "alpha") };
        IdentifierComposer.Compose("Widget", p, Storage(max: 63)).Should().Be("Widget#alpha");
    }

    [Fact]
    public void Single_particle_path_allocates_only_the_result_string()
    {
        var p = new[] { new Particle(0, "partition", "alpha") };
        var policy = Storage();
        IdentifierComposer.Compose("Widget", p, policy); // warm up

        const int iters = 10_000;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iters; i++) IdentifierComposer.Compose("Widget", p, policy);
        var perCall = (GC.GetAllocatedBytesForCurrentThread() - before) / (double)iters;

        // One ~12-char result string per call (~48 bytes). A per-call List/array/closure/StringBuilder
        // regression on the single-particle path would blow well past this bound.
        perCall.Should().BeLessThan(80);
    }

    // --- ARCH-0096 realignment: positional particles (leading container prefix vs trailing partition suffix) ---

    [Fact]
    public void Leading_particle_prepends_token_before_the_anchor()
    {
        var p = new[] { new Particle(0, "tenant", "2a6v7", ParticlePosition.Leading) };
        IdentifierComposer.Compose("Todo", p, Storage()).Should().Be("2a6v7#Todo");
    }

    [Fact]
    public void Leading_particle_honours_its_own_separator_override()
    {
        var p = new[] { new Particle(0, "tenant", "2a6v7", ParticlePosition.Leading, ".") };
        IdentifierComposer.Compose("Todo", p, Storage()).Should().Be("2a6v7.Todo");   // the container-per-tenant shape
    }

    [Fact]
    public void Leading_and_trailing_compose_around_the_anchor()
    {
        var p = new[]
        {
            new Particle(0, "tenant", "2a6v7", ParticlePosition.Leading, "."),   // tenant container prefix
            new Particle(0, "partition", "alpha"),                                // partition suffix, policy '#'
        };
        IdentifierComposer.Compose("Todo", p, Storage()).Should().Be("2a6v7.Todo#alpha");
    }

    [Fact]
    public void An_omitted_leading_particle_returns_the_anchor_reference()
    {
        var anchor = "Todo";
        var p = new[] { new Particle(0, "tenant", "", ParticlePosition.Leading, ".") };
        IdentifierComposer.Compose(anchor, p, Storage()).Should().BeSameAs(anchor);
    }

    [Fact]
    public void Multiple_leading_particles_compose_in_order()
    {
        var p = new[]
        {
            new Particle(1, "b", "two", ParticlePosition.Leading),
            new Particle(0, "a", "one", ParticlePosition.Leading),
        };
        IdentifierComposer.Compose("X", p, Storage()).Should().Be("one#two#X");
    }

    [Fact]
    public void Leading_clamp_preserves_the_prefix_and_uniqueness_per_value()
    {
        var anchor = new string('t', 80);
        var a = new[] { new Particle(0, "tenant", "2a6v7", ParticlePosition.Leading, ".") };
        var b = new[] { new Particle(0, "tenant", "234vd", ParticlePosition.Leading, ".") };

        var ra = IdentifierComposer.Compose(anchor, a, Storage(max: 40));
        var rb = IdentifierComposer.Compose(anchor, b, Storage(max: 40));

        Encoding.UTF8.GetByteCount(ra).Should().BeLessThanOrEqualTo(40);
        ra.Should().StartWith("2a6v7");   // the leading tenant survives the clamp head
        ra.Should().NotBe(rb);            // two tenants never collide, even when clamped (hash of the full id)
    }
}
