using System.Reflection;
using Koan.Media.Web.Options;
using Koan.Media.Web.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Koan.Media.Core.Tests.Support;

/// <summary>
/// Minimal ASP.NET test server hosting the MediaController for
/// integration tests. Spins up a real Kestrel pipeline (via
/// <see cref="TestServer"/>) with an <see cref="InMemoryMediaSource"/>
/// and the recipe registry. No external services touched.
/// </summary>
public sealed class MediaTestServer : IAsyncDisposable
{
    public InMemoryMediaSource Source { get; }
    public WebApplication App { get; }
    public HttpClient Client { get; }

    private MediaTestServer(InMemoryMediaSource source, WebApplication app, HttpClient client)
    {
        Source = source;
        App = app;
        Client = client;
    }

    public static async Task<MediaTestServer> StartAsync(
        Action<IServiceCollection>? configureServices = null,
        Dictionary<string, string?>? settings = null,
        Assembly[]? scanAssemblies = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();

        if (settings is not null)
        {
            builder.Configuration.AddInMemoryCollection(settings);
        }

        builder.Services.AddControllers()
            .AddApplicationPart(typeof(Koan.Media.Web.Controllers.MediaController).Assembly);

        var inMemorySource = new InMemoryMediaSource();
        builder.Services.AddSingleton<IMediaSource>(inMemorySource);

        // Default IOverlayResolver registration so the controller can
        // serve overlay requests in tests without extra setup.
        builder.Services.TryAddSingleton<Koan.Media.Abstractions.Recipes.IOverlayResolver,
            Koan.Media.Web.Routing.DefaultOverlayResolver>();

        // Bind MediaWebOptions from the InMemoryCollection
        builder.Services.AddOptions<MediaWebOptions>()
            .BindConfiguration(MediaWebOptions.SectionPath);

        // Recipe registry: wire IOptionsMonitor + assemblies for [MediaRecipe] scan
        builder.Services.AddOptions<RecipesOptions>()
            .BindConfiguration(RecipesOptions.SectionPath);
        builder.Services.TryAddSingleton<Koan.Media.Abstractions.Recipes.IMediaRecipeRegistry>(sp =>
        {
            var monitor = sp.GetService<IOptionsMonitor<RecipesOptions>>();
            var assemblies = scanAssemblies ?? new[] { typeof(MediaTestServer).Assembly };
            return new MediaRecipeRegistry(assemblies, monitor, NullLogger<MediaRecipeRegistry>.Instance);
        });

        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();

        var client = app.GetTestClient();
        return new MediaTestServer(inMemorySource, app, client);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.DisposeAsync();
    }
}
