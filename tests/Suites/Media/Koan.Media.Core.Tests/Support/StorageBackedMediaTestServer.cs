using System.Reflection;
using Koan.Media.Web.Options;
using Koan.Media.Web.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Koan.Media.Core.Tests.Support;

/// <summary>
/// Variant of <see cref="MediaTestServer"/> wired to a
/// <see cref="StorageBackedMediaSource"/> so the MEDIA-0007 cache-as-storage
/// path can be exercised end-to-end without depending on Koan.Storage. The
/// host stack is otherwise identical to <see cref="MediaTestServer"/>.
/// </summary>
public sealed class StorageBackedMediaTestServer : IAsyncDisposable
{
    public StorageBackedMediaSource Source { get; }
    public WebApplication App { get; }
    public HttpClient Client { get; }

    private StorageBackedMediaTestServer(StorageBackedMediaSource source, WebApplication app, HttpClient client)
    {
        Source = source;
        App = app;
        Client = client;
    }

    public static async Task<StorageBackedMediaTestServer> StartAsync(
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

        var source = new StorageBackedMediaSource();
        builder.Services.AddSingleton<IMediaSource>(source);

        builder.Services.TryAddSingleton<Koan.Media.Abstractions.Recipes.IOverlayResolver,
            Koan.Media.Web.Routing.DefaultOverlayResolver>();

        builder.Services.AddOptions<MediaWebOptions>()
            .BindConfiguration(MediaWebOptions.SectionPath);
        builder.Services.AddOptions<RecipesOptions>()
            .BindConfiguration(RecipesOptions.SectionPath);
        builder.Services.TryAddSingleton<Koan.Media.Abstractions.Recipes.IMediaRecipeRegistry>(sp =>
        {
            var monitor = sp.GetService<IOptionsMonitor<RecipesOptions>>();
            var assemblies = scanAssemblies ?? Array.Empty<Assembly>();
            return new MediaRecipeRegistry(assemblies, monitor, NullLogger<MediaRecipeRegistry>.Instance);
        });

        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();

        var client = app.GetTestClient();
        return new StorageBackedMediaTestServer(source, app, client);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.DisposeAsync();
    }
}
