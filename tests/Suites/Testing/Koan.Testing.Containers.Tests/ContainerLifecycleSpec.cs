using AwesomeAssertions;
using Koan.Testing.Containers;

namespace Koan.Testing.Containers.Tests;

public sealed class ContainerLifecycleSpec
{
    [Fact]
    public async Task Startup_failure_runs_cleanup_and_preserves_the_original_error()
    {
        var fixture = new ProbeFixture(failStart: true, failStop: false);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await fixture.InitializeAsync());

        failure.Message.Should().Be(ProbeFixture.StartFailureMessage);
        fixture.StopCount.Should().Be(1);
        fixture.IsAvailable.Should().BeFalse();
        fixture.Reason.Should().Contain(ProbeFixture.StartFailureMessage);
    }

    [Fact]
    public async Task Startup_and_cleanup_failures_are_both_reported()
    {
        var fixture = new ProbeFixture(failStart: true, failStop: true);

        var failure = await Assert.ThrowsAsync<AggregateException>(
            async () => await fixture.InitializeAsync());

        failure.InnerExceptions.Select(exception => exception.Message)
            .Should().Contain([ProbeFixture.StartFailureMessage, ProbeFixture.StopFailureMessage]);
        fixture.StopCount.Should().Be(1);
    }

    [Fact]
    public async Task Teardown_failure_is_not_swallowed()
    {
        var fixture = new ProbeFixture(failStart: false, failStop: true);
        await fixture.InitializeAsync();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await fixture.DisposeAsync());

        failure.Message.Should().Be(ProbeFixture.StopFailureMessage);
        fixture.StopCount.Should().Be(1);
    }

    private sealed class ProbeFixture(bool failStart, bool failStop) : KoanContainerFixture
    {
        internal const string StartFailureMessage = "probe-start-failure";
        internal const string StopFailureMessage = "probe-stop-failure";

        public override string Engine => "probe";
        protected override string Adapter => "inmemory";
        internal int StopCount { get; private set; }

        protected override Task<string> StartContainerAsync() => failStart
            ? Task.FromException<string>(new InvalidOperationException(StartFailureMessage))
            : Task.FromResult("memory://probe");

        protected override ValueTask StopContainerAsync()
        {
            StopCount++;
            return failStop
                ? ValueTask.FromException(new InvalidOperationException(StopFailureMessage))
                : ValueTask.CompletedTask;
        }
    }
}
