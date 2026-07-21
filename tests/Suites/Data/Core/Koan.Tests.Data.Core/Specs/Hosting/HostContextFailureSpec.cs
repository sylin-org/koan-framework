using AwesomeAssertions;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Tests.Data.Core.Support;

namespace Koan.Tests.Data.Core.Specs.Hosting;

public sealed class HostContextFailureSpec : IDisposable
{
    private readonly IServiceProvider? _initialHost = AppHost.Current;

    public HostContextFailureSpec()
    {
        AppHost.Current = null;
    }

    public void Dispose()
    {
        AppHost.Current = _initialHost;
    }

    [Fact]
    public void Entity_data_without_a_host_reports_the_required_data_service()
    {
        var act = () => Data<TodoEntity, string>.Capabilities;

        var error = act.Should().Throw<KoanHostContextException>().Which;
        error.Failure.Should().Be(KoanHostContextException.FailureKind.MissingHost);
        error.Operation.Should().Be("entity data access");
        error.RequiredService.Should().Be(typeof(IDataService));
    }
}
