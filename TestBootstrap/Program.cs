using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Sora.Data.Core;

Console.WriteLine("=== Testing Enhanced Sora Bootstrap Decision Logging ===");
Console.WriteLine();

// Set container environment to see container-based decisions
Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", "true");
Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

// Create a basic configuration with some settings to test decision logic
var configBuilder = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        {"Sora:Bootstrap:ShowDecisions", "true"},
        {"Sora:Bootstrap:ShowConnectionAttempts", "true"}, 
        {"Sora:Bootstrap:ShowDiscovery", "true"},
        {"Sora:Bootstrap:CompactMode", "false"}
    })
    .AddEnvironmentVariables();

var configuration = configBuilder.Build();

var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(configuration);

// This will trigger all the enhanced BootReport decision logging we implemented
services.AddSora();

var serviceProvider = services.BuildServiceProvider();

// Manually trigger the discovery process to see our enhanced BootReport
var appRuntime = serviceProvider.GetService<Sora.Core.Hosting.Runtime.IAppRuntime>();
appRuntime?.Discover();

Console.WriteLine();
Console.WriteLine("=== Enhanced Bootstrap Decision Logging Complete ===");
