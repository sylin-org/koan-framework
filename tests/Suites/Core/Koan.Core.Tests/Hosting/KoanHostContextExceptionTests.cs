using AwesomeAssertions;
using Koan.Core.Hosting.App;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Core.Tests.Hosting;

[Collection(nameof(AppHostScopeTests))]
public sealed class KoanHostContextExceptionTests : IDisposable
{
    private const string Operation = "host context contract probe";

    private readonly IServiceProvider? _initialHost = AppHost.Current;

    public KoanHostContextExceptionTests()
    {
        AppHost.Current = null;
    }

    public void Dispose()
    {
        AppHost.Current = _initialHost;
    }

    [Fact]
    public void Required_service_without_a_host_reports_corrective_context()
    {
        var act = () => AppHost.GetRequiredService<ProbeService>(Operation);

        var error = act.Should().Throw<KoanHostContextException>().Which;
        error.Failure.Should().Be(KoanHostContextException.FailureKind.MissingHost);
        error.Operation.Should().Be(Operation);
        error.RequiredService.Should().Be(typeof(ProbeService));
        error.Message.Should().Contain("AddKoan").And.Contain("StartKoan").And.Contain("PushScope");
    }

    [Fact]
    public void Required_service_from_a_disposed_host_preserves_the_cause()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        AppHost.Current = provider;
        provider.Dispose();

        var act = () => AppHost.GetRequiredService<ProbeService>(Operation);

        var error = act.Should().Throw<KoanHostContextException>().Which;
        error.Failure.Should().Be(KoanHostContextException.FailureKind.DisposedHost);
        error.InnerException.Should().BeOfType<ObjectDisposedException>();
    }

    [Fact]
    public void Required_service_missing_from_an_active_host_names_the_service()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        AppHost.Current = provider;

        var act = () => AppHost.GetRequiredService<ProbeService>(Operation);

        var error = act.Should().Throw<KoanHostContextException>().Which;
        error.Failure.Should().Be(KoanHostContextException.FailureKind.MissingService);
        error.RequiredService.Should().Be(typeof(ProbeService));
        error.Message.Should().Contain(typeof(ProbeService).FullName!);
    }

    [Fact]
    public void Required_service_does_not_relabel_construction_failure()
    {
        using var provider = new ServiceCollection()
            .AddSingleton<ProbeService>(_ => throw new ProbeConstructionException())
            .BuildServiceProvider();
        AppHost.Current = provider;

        var act = () => AppHost.GetRequiredService<ProbeService>(Operation);

        act.Should().Throw<ProbeConstructionException>();
    }

    private sealed class ProbeService;
    private sealed class ProbeConstructionException : Exception;
}
