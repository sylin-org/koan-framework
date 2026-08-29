using Koan.Core.Provenance;
using Koan.Data.Connector.CouchDb.Initialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Koan.Data.Connector.CouchDb.Tests.Specs;

/// <summary>Boot description is observation only; it never contacts an unelected CouchDB server.</summary>
public sealed class CouchDbBootProvenanceSpec
{
    [Fact]
    public void Module_reporting_is_connection_free()
    {
        var configuration = new ConfigurationBuilder().Build();
        var registry = ProvenanceRegistry.Instance;
        var module = registry.GetOrCreateModule("data", "Koan.Data.Connector.CouchDb");

        new CouchDbModule().Report(module, configuration, new TestHostEnvironment());

        registry.CurrentSnapshot.FindModule("data", "Koan.Data.Connector.CouchDb").Should().NotBeNull();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Koan.Data.Connector.CouchDb.Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } =
            new PhysicalFileProvider(Path.GetTempPath());
    }
}
