using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Core.Hosting.App;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using S7.Meridian.Tests.Fakes;
using Koan.AI.Contracts;
using Koan.Samples.Meridian.Services;

namespace S7.Meridian.Tests;

public sealed class MeridianWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((context, configurationBuilder) =>
        {
            var ollamaUrl = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL") ?? "http://127.0.0.1:11434";
            var defaultModel = Environment.GetEnvironmentVariable("OLLAMA_DEFAULT_MODEL") ?? "granite3.3:8b";

            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Environment"] = "Test",
                ["Koan:Data:Sources:Default:Adapter"] = "memory",
                ["Koan:Data:Vector:EnableWorkflows"] = "false",
                ["Koan:BackgroundServices:Enabled"] = "false",
                ["Logging:EventLog:LogLevel:Default"] = "None",
                ["Koan:Ai:Ollama:Urls:0"] = ollamaUrl,
                ["Koan:Ai:Ollama:DefaultModel"] = defaultModel,
                ["Koan:Ai:Ollama:RequiredModels:0"] = defaultModel,
                ["Meridian:Extraction:Ocr:Enabled"] = "false"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Ensure AppHost is reset so the factory host becomes the active context.
            AppHost.Current = null;

            // Replace external AI dependencies with deterministic test doubles.
            services.RemoveAll<IAi>();
            services.AddSingleton<IAi, FakeAuthoringAi>();

            // Remove the Meridian job worker hosted service to avoid background polling in tests.
            var hostedDescriptors = services
                .Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(MeridianJobWorker))
                .ToList();
            foreach (var descriptor in hostedDescriptors)
            {
                services.Remove(descriptor);
            }
        });
    }
}
