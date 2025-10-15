using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Koan.Core;

public interface IKoanAutoRegistrar : IKoanInitializer
{
    string ModuleName { get; }
    string? ModuleVersion { get; }
    void Describe(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env);
}