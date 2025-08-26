using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Sora.Core;

public interface ISoraAutoRegistrar : ISoraInitializer
{
    string ModuleName { get; }
    string? ModuleVersion { get; }
    void Describe(Sora.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env);
}