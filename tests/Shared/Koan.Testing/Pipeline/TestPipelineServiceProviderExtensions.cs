using Microsoft.Extensions.DependencyInjection;
using Koan.Testing.Contracts;
using Koan.Testing.Fixtures;

namespace Koan.Testing.Pipeline;

public static class TestPipelineServiceProviderExtensions
{
    public static TestPipeline UsingServiceProvider(this TestPipeline pipeline, string key = "services", Action<TestContext, IServiceCollection>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        return pipeline.Using(key, _ => ValueTask.FromResult(new ServiceProviderFixture(configure)));
    }
}
