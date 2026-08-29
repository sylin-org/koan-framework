using Koan.Core.Provenance;
using Koan.Data.Connector.Firebird.Initialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Koan.Data.Connector.Firebird.Tests.Specs;

/// <summary>Boot description is observation only; it never contacts an unelected Firebird server.</summary>
public sealed class FirebirdBootProvenanceSpec
{
    [Fact]
    public void Module_reporting_is_connection_free()
    {
        var configuration = new ConfigurationBuilder().Build();
        var registry = ProvenanceRegistry.Instance;
        var module = registry.GetOrCreateModule("data", "Koan.Data.Connector.Firebird");

        new FirebirdModule().Report(module, configuration, new TestHostEnvironment());

        registry.CurrentSnapshot.FindModule("data", "Koan.Data.Connector.Firebird").Should().NotBeNull();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Koan.Data.Connector.Firebird.Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } =
            new PhysicalFileProvider(Path.GetTempPath());
    }
}
