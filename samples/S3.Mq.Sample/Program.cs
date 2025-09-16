using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using S3.Mq.Sample;
using Koan.Core;
using Koan.Data.Core;
using Koan.Messaging;

var services = new ServiceCollection();

// Minimal configuration for RabbitMQ
var cfg = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();
services.AddSingleton<IConfiguration>(cfg);

// Add Koan with discovery; RabbitMQ initializer registers itself when referenced
services.AddKoan();

// register handlers with simple chaining
// [REMOVED obsolete OnMessage/OnBatch handlers]

// [REMOVED obsolete semantic builder example]

// Build and start
var sp = services.BuildServiceProvider();
Koan.Core.Hosting.App.AppHost.Current = sp;
try { Koan.Core.KoanEnv.TryInitialize(sp); } catch { }
(sp.GetService(typeof(Koan.Core.Hosting.Runtime.IAppRuntime)) as Koan.Core.Hosting.Runtime.IAppRuntime)?.Discover();
(sp.GetService(typeof(Koan.Core.Hosting.Runtime.IAppRuntime)) as Koan.Core.Hosting.Runtime.IAppRuntime)?.Start();

Console.WriteLine("S3.Mq.Sample: sending two test messages...");

await new Hello { Name = "Koan" }.Send();
await new UserRegistered { UserId = "u-1", Email = "u1@example.com" }.Send();

// send a batch (grouped) example
var moreUsers = new List<UserRegistered>
{
    new() { UserId = "u-2", Email = "u2@example.com" },
    new() { UserId = "u-3", Email = "u3@example.com" },
    new() { UserId = "u-4", Email = "u4@example.com" }
};
// [REMOVED obsolete SendAsBatch usage]

Console.WriteLine("Message sent. Listening... Press Ctrl+C to exit.");

await Task.Delay(-1);

namespace S3.Mq.Sample
{
    // ...existing code...
}