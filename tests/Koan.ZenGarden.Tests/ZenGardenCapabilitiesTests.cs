using AwesomeAssertions;
using Koan.ZenGarden.Models;
using Xunit;

namespace Koan.ZenGarden.Tests;

public sealed class ZenGardenCapabilitiesTests : IClassFixture<ZenGardenFixture>
{
    private readonly ZenGardenFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ZenGardenCapabilitiesTests(ZenGardenFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task Catalog_Offerings_IsReachable_AndNormalizesShape()
    {
        EnsureGardenAvailable();

        var tools = await _fixture.Client.Catalog(new ZenGardenSubscription
        {
            ToolType = ZenGardenToolType.Offering
        });

        tools.Should().NotBeNull();
        tools.Should().OnlyContain(x => x.ToolType == ZenGardenToolType.Offering);
        tools.Should().OnlyContain(x => !string.IsNullOrEmpty(x.ToolFqid));
    }

    [Fact]
    public async Task Catalog_Storage_IsReachable_AndNormalizesShape()
    {
        EnsureGardenAvailable();

        var tools = await _fixture.Client.Catalog(new ZenGardenSubscription
        {
            ToolType = ZenGardenToolType.SeedBank
        });

        tools.Should().NotBeNull();
        tools.Should().OnlyContain(x => x.ToolType == ZenGardenToolType.SeedBank);
        tools.Should().OnlyContain(x => !string.IsNullOrEmpty(x.ToolFqid));
    }

    [Fact]
    public async Task Subscribe_Offering_EmitsInitialAvailabilityEvent()
    {
        EnsureGardenAvailable();

        var selected = await SelectOffering();
        if (selected is null)
        {
            _output.WriteLine("No offering available in garden; nothing to validate for offering subscription.");
            Assert.Skip("No offering is available in the garden for the subscription probe.");
            return;
        }

        var offeringName = selected.ToolFqid;
        var firstEvent = await CaptureInitialEvent(
            ZenGardenSubscription.ForOffering(offeringName),
            TimeSpan.FromSeconds(15));

        firstEvent.Current.ToolFqid.Should().Be(selected.ToolFqid);
        firstEvent.Current.ToolType.Should().Be(ZenGardenToolType.Offering);
        firstEvent.Kind.Should().BeOneOf(
            ZenGardenAvailabilityEventKind.Online,
            ZenGardenAvailabilityEventKind.Offline);
    }

    [Fact]
    public async Task Subscribe_Offering_WithCapabilityRequirement_EmitsCapabilityEvent()
    {
        EnsureGardenAvailable();

        var offerings = await _fixture.Client.Catalog(new ZenGardenSubscription
        {
            ToolType = ZenGardenToolType.Offering
        });

        var selected = offerings.FirstOrDefault(x =>
            x.Capabilities.Any(capability => capability.Value.Count > 0));
        if (selected is null)
        {
            _output.WriteLine("No offering with capabilities found; capability event test skipped.");
            Assert.Skip("No offering with capabilities is available in the garden.");
            return;
        }

        var capability = selected.Capabilities
            .First(entry => entry.Value.Count > 0);
        var token = capability.Value[0];
        var offeringName = selected.ToolFqid;

        var firstEvent = await CaptureInitialEvent(
            ZenGardenSubscription.ForOffering(offeringName).Require(token),
            TimeSpan.FromSeconds(30));

        firstEvent.Current.ToolFqid.Should().Be(selected.ToolFqid);
        firstEvent.Kind.Should().BeOneOf(
            ZenGardenAvailabilityEventKind.CapabilitiesSatisfied,
            ZenGardenAvailabilityEventKind.CapabilitiesUnsatisfied);
    }

    [Fact]
    public async Task Subscribe_Storage_EmitsInitialAvailabilityEvent()
    {
        EnsureGardenAvailable();

        var storageTools = await _fixture.Client.Catalog(new ZenGardenSubscription
        {
            ToolType = ZenGardenToolType.SeedBank
        });

        var selected = storageTools.FirstOrDefault();
        if (selected is null)
        {
            _output.WriteLine("No seed-bank available in garden; storage subscription test skipped.");
            Assert.Skip("No seed-bank is available in the garden for the subscription probe.");
            return;
        }

        var seedBankName = ToolFqid.Parse(selected.ToolFqid).ToString();
        var firstEvent = await CaptureInitialEvent(
            ZenGardenSubscription.ForStorage(seedBankName),
            TimeSpan.FromSeconds(15));

        firstEvent.Current.ToolFqid.Should().Be(selected.ToolFqid);
        firstEvent.Current.ToolType.Should().Be(ZenGardenToolType.SeedBank);
        firstEvent.Kind.Should().BeOneOf(
            ZenGardenAvailabilityEventKind.Online,
            ZenGardenAvailabilityEventKind.Offline);
    }

    private async Task<ZenGardenToolSnapshot?> SelectOffering()
    {
        var preferred = await _fixture.Client.Catalog(
            ZenGardenSubscription.ForOffering(_fixture.PreferredOffering));
        if (preferred.Count > 0)
        {
            return preferred[0];
        }

        var all = await _fixture.Client.Catalog(new ZenGardenSubscription
        {
            ToolType = ZenGardenToolType.Offering
        });
        return all.FirstOrDefault();
    }

    private async Task<ZenGardenAvailabilityEvent> CaptureInitialEvent(
        ZenGardenSubscription subscription,
        TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<ZenGardenAvailabilityEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var registration = timeoutCts.Token.Register(() => tcs.TrySetCanceled(timeoutCts.Token));
        using var watcher = _fixture.Client.Subscribe(
            subscription,
            (evt, _) =>
            {
                tcs.TrySetResult(evt);
                return ValueTask.CompletedTask;
            },
            new ZenGardenWatchOptions { EmitInitialState = true });

        return await tcs.Task;
    }

    private void EnsureGardenAvailable()
    {
        if (_fixture.IsAvailable)
        {
            return;
        }

        var reason = string.IsNullOrWhiteSpace(_fixture.UnavailableReason)
            ? "unknown"
            : _fixture.UnavailableReason;
        var message = $"Zen Garden endpoint resolution '{_fixture.EndpointDisplay}' unavailable: {reason}";

        if (_fixture.RequireAvailable)
        {
            Assert.Fail(message);
        }

        _output.WriteLine(message);
        _output.WriteLine("Set KOAN_TESTS_ZENGARDEN_REQUIRED=1 to make this a hard failure.");
        Assert.Skip(message);
    }

}
