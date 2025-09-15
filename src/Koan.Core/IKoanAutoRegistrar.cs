using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Koan.Core;

public interface IKoanAutoRegistrar : IKoanInitializer
{
    string ModuleName { get; }
    string? ModuleVersion { get; }
    void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env);
}