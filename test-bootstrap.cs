using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Koan.Data.Core;
using System;
using System.IO;

Console.WriteLine("=== Testing Enhanced Koan Bootstrap Decision Logging ===");
Console.WriteLine();

// Set container environment to see container-based decisions
Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", "true");

// Create a basic configuration with some settings to test decision logic
var configBuilder = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddInMemoryCollection(new Dictionary<string, string>
    {
        {"Koan:Bootstrap:ShowDecisions", "true"},
        {"Koan:Bootstrap:ShowConnectionAttempts", "true"}, 
        {"Koan:Bootstrap:ShowDiscovery", "true"},
        {"Koan:Bootstrap:CompactMode", "false"},
        {"ASPNETCORE_ENVIRONMENT", "Development"}
    })
    .AddEnvironmentVariables();

var configuration = configBuilder.Build();

var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(configuration);

// This will trigger all the enhanced BootReport decision logging we implemented
services.AddKoan();

var serviceProvider = services.BuildServiceProvider();

// Manually trigger the discovery process to see our enhanced BootReport
var appRuntime = serviceProvider.GetService<Koan.Core.Hosting.Runtime.IAppRuntime>();
appRuntime?.Discover();

Console.WriteLine();
Console.WriteLine("=== Enhanced Bootstrap Decision Logging Complete ===");