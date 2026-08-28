using Koan.Core.Provenance;
using Koan.Data.Connector.DuckDb.Initialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Koan.Data.Connector.DuckDb.Tests.Specs;

/// <summary>Boot description is observation only; it never provisions an unelected DuckDB target.</summary>
public sealed class DuckDbBootProvenanceSpec
{
    [Fact]
    public void Auto_connection_is_reported_without_creating_the_local_fallback()
    {
        var root = Path.Combine(Path.GetTempPath(), $"koan-duckdb-provenance-{Guid.CreateVersion7():n}");
        Directory.CreateDirectory(root);
        var previousDirectory = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = root;
            var configuration = new ConfigurationBuilder().Build();
            var registry = ProvenanceRegistry.Instance;
            var module = registry.GetOrCreateModule("data", "Koan.Data.Connector.DuckDb");

            new DuckDbModule().Report(module, configuration, new TestHostEnvironment(root));

            Directory.Exists(Path.Combine(root, ".koan")).Should().BeFalse();
            var connection = registry.CurrentSnapshot.FindModule("data", "Koan.Data.Connector.DuckDb")!
                .Settings.Single(setting => setting.Key == "Koan:Data:DuckDb:ConnectionString");
            connection.Value.Should().Be("auto");
            connection.Source.Should().Be(ProvenanceSettingSource.Auto);
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class TestHostEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Koan.Data.Connector.DuckDb.Tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRoot);
    }
}
