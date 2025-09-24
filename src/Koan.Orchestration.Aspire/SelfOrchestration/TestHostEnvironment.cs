using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Koan.Orchestration.Aspire.SelfOrchestration;

/// <summary>
/// Simple test host environment for backward compatibility scenarios
/// </summary>
internal class TestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Development";
    public string ApplicationName { get; set; } = "TestApp";
    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}