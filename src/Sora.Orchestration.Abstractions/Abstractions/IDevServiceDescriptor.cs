using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Sora.Orchestration;

public interface IDevServiceDescriptor
{
    (bool Active, string? Reason) ShouldApply(IConfiguration cfg, IHostEnvironment env);
    ServiceSpec Describe(Profile profile, IConfiguration cfg);
}
