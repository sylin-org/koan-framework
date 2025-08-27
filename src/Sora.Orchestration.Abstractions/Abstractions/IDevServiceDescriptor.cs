using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Sora.Orchestration.Models;

namespace Sora.Orchestration.Abstractions;

public interface IDevServiceDescriptor
{
    (bool Active, string? Reason) ShouldApply(IConfiguration cfg, IHostEnvironment env);
    ServiceSpec Describe(Profile profile, IConfiguration cfg);
}
