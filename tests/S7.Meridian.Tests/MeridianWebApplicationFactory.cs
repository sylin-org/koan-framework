using System;
using System.Collections.Generic;
using Koan.Core.Hosting.App;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
                ["Koan:Ai:Ollama:Urls:0"] = ollamaUrl,
                ["Koan:Ai:Ollama:DefaultModel"] = defaultModel,
                ["Koan:Ai:Ollama:RequiredModels:0"] = defaultModel
            });
        });

        builder.ConfigureServices(services =>
        {
            // Ensure AppHost is reset so the factory host becomes the active context.
            AppHost.Current = null;
        });
    }
}
