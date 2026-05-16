using Koan.Jobs.RateGating;

namespace Koan.Jobs.Core.Tests.Specs;

public sealed class RateLimitedJobExceptionSpec
{
    [Fact]
    public void Constructor_captures_host_tag_and_retry_after()
    {
        var ex = new RateLimitedJobException("nexusmods", TimeSpan.FromSeconds(120));
        ex.HostTag.Should().Be("nexusmods");
        ex.RetryAfter.Should().Be(TimeSpan.FromSeconds(120));
    }

    [Fact]
    public void Default_message_includes_host_and_duration()
    {
        var ex = new RateLimitedJobException("nexusmods", TimeSpan.FromSeconds(60));
        ex.Message.Should().Contain("nexusmods").And.Contain("60");
    }

    [Fact]
    public void Custom_message_is_preserved()
    {
        var ex = new RateLimitedJobException("nexusmods", TimeSpan.FromMinutes(1), "custom reason");
        ex.Message.Should().Be("custom reason");
    }

    [Fact]
    public void Inner_exception_is_preserved()
    {
        var inner = new InvalidOperationException("upstream said no");
        var ex = new RateLimitedJobException("nexusmods", TimeSpan.FromMinutes(1), inner: inner);
        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void Empty_host_tag_throws()
    {
        var act = () => new RateLimitedJobException("", TimeSpan.FromMinutes(1));
        act.Should().Throw<ArgumentException>().WithParameterName("hostTag");
    }

    [Fact]
    public void Zero_or_negative_retry_after_throws()
    {
        var zero = () => new RateLimitedJobException("nexusmods", TimeSpan.Zero);
        var negative = () => new RateLimitedJobException("nexusmods", TimeSpan.FromSeconds(-1));
        zero.Should().Throw<ArgumentException>().WithParameterName("retryAfter");
        negative.Should().Throw<ArgumentException>().WithParameterName("retryAfter");
    }
}
