using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Testing.Integration;
using Koan.Web.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Web.OpenGraph.Tests;

/// <summary>Readable by anyone the resolver admits: its own Hidden flag is the application's rule.</summary>
public sealed class ResolvedWork : Entity<ResolvedWork>
{
    public string Name { get; set; } = "";
    public bool Hidden { get; set; }
}

/// <summary>Its read gate admits only signed-in readers.</summary>
[Access(read: Access.Authenticated)]
public sealed class GatedWork : Entity<GatedWork>
{
    public string Name { get; set; } = "";
}

/// <summary>Its read constraint admits only the rows marked public.</summary>
public sealed class ConstrainedWork : Entity<ConstrainedWork>
{
    public string Name { get; set; } = "";
    public bool Public { get; set; }
}

public sealed class ConstrainedWorkAccess : EntityAccess<ConstrainedWork>
{
    public override IAccessFilter<ConstrainedWork> Constrain(IAccessFilter<ConstrainedWork> q, AccessAction action)
        => action == AccessAction.Read ? q.Where(work => work.Public) : q;
}

/// <summary>Routed by a slug rather than its id.</summary>
public sealed class SluggedWork : Entity<SluggedWork>
{
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>
/// WEB-0083: a link preview shows what the request's reader may read, read now: the application's resolver, then the
/// Entity's read gate and read constraint for that reader, on every navigation, whichever token routes it.
/// </summary>
[Collection(AmbientHost.Name)]
public sealed class ReaderCardSpec : IAsyncLifetime
{
    private const string DefaultTitle = "<meta property=\"og:title\" content=\"Test Site\" />";

    private IntegrationHost _host = default!;
    private IServiceProvider? _previousAppHost;
    private string _shellPath = default!;

    public async ValueTask InitializeAsync()
    {
        _shellPath = Path.Combine(Path.GetTempPath(), $"koan-og-reader-{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(_shellPath, "<html><head><!--KOAN_OPENGRAPH--></head><body>app</body></html>");

        _host = await KoanIntegrationHost.Configure()
            .WithSetting("Koan:Environment", "Test")
            .WithSetting("Koan:Data:Sources:Default:Adapter", "inmemory")
            .WithSetting("Koan:Web:OpenGraph:ShellPath", _shellPath)
            .WithSetting("Koan:Web:OpenGraph:SiteName", "Test Site")
            .ConfigureServices(s =>
            {
                s.AddKoan(() =>
                {
                    SocialCards.For<ResolvedWork>("/resolved/{id}", async id =>
                            await ResolvedWork.Get(id) is { Hidden: false } work ? work : null)
                        .Title(work => work.Name);
                    SocialCards.For<GatedWork>("/gated/{id}", id => GatedWork.Get(id))
                        .Title(work => work.Name);
                    SocialCards.For<ConstrainedWork>("/constrained/{id}", id => ConstrainedWork.Get(id))
                        .Title(work => work.Name);
                    SocialCards.For<SluggedWork>("/slugged/{slug}", async slug =>
                            (await SluggedWork.Query(work => work.Slug == slug)).FirstOrDefault())
                        .Title(work => work.Name);
                });
                s.AddScoped<EntityAccess<ConstrainedWork>, ConstrainedWorkAccess>();
            })
            .StartAsync();

        _previousAppHost = AppHost.Current;
        AppHost.Current = _host.Services;
    }

    public async ValueTask DisposeAsync()
    {
        AppHost.Current = _previousAppHost!;
        await _host.DisposeAsync();
        try { File.Delete(_shellPath); } catch { /* best-effort */ }
    }

    private Task<string?> Render(string path, ClaimsPrincipal? reader = null)
        => _host.Services.GetRequiredService<IOpenGraphCardRenderer>().RenderShellAsync(Navigation(path, reader));

    private HttpRequest Navigation(string path, ClaimsPrincipal? reader)
    {
        var context = new DefaultHttpContext { RequestServices = _host.Services.CreateScope().ServiceProvider };
        if (reader is not null) context.User = reader;
        context.Request.Method = "GET";
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("example.org");
        context.Request.Path = path;
        context.Request.Headers.Accept = "text/html";
        return context.Request;
    }

    private static ClaimsPrincipal SignedIn()
        => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "reader")], "test"));

    private static string Title(string name) => $"<meta property=\"og:title\" content=\"{name}\" />";

    [Fact]
    public async Task A_row_the_resolver_refuses_shows_the_default_card_even_after_it_was_saved()
    {
        var work = await new ResolvedWork { Name = "Secret plans", Hidden = true }.Save();

        var html = await Render($"/resolved/{work.Id}");

        html.Should().NotContain("Secret plans").And.Contain(DefaultTitle);
    }

    [Fact]
    public async Task A_row_that_turns_private_stops_showing_its_card()
    {
        var work = await new ResolvedWork { Name = "Once public" }.Save();
        (await Render($"/resolved/{work.Id}")).Should().Contain(Title("Once public"));

        work.Hidden = true;
        await work.Save();

        (await Render($"/resolved/{work.Id}")).Should().NotContain("Once public").And.Contain(DefaultTitle);
    }

    [Fact]
    public async Task The_entity_s_read_gate_decides_for_the_reader()
    {
        var work = await new GatedWork { Name = "Members only" }.Save();

        (await Render($"/gated/{work.Id}")).Should().NotContain("Members only").And.Contain(DefaultTitle);
        (await Render($"/gated/{work.Id}", SignedIn())).Should().Contain(Title("Members only"));
    }

    [Fact]
    public async Task The_entity_s_read_constraint_decides_for_the_row()
    {
        var hidden = await new ConstrainedWork { Name = "Row held back", Public = false }.Save();
        var shown = await new ConstrainedWork { Name = "Row in the open", Public = true }.Save();

        (await Render($"/constrained/{hidden.Id}")).Should().NotContain("Row held back").And.Contain(DefaultTitle);
        (await Render($"/constrained/{shown.Id}")).Should().Contain(Title("Row in the open"));
    }

    [Fact]
    public async Task A_card_routed_by_a_slug_follows_its_entity_and_goes_with_it()
    {
        var work = await new SluggedWork { Slug = "the-slug", Name = "First words" }.Save();
        (await Render("/slugged/the-slug")).Should().Contain(Title("First words"));

        work.Name = "Second words";
        await work.Save();
        (await Render("/slugged/the-slug")).Should().Contain(Title("Second words"));

        await work.Remove();
        (await Render("/slugged/the-slug")).Should().NotContain("Second words").And.Contain(DefaultTitle);
    }

    [Fact]
    public async Task A_card_rendered_for_a_signed_in_reader_is_not_for_a_shared_cache()
    {
        var work = await new GatedWork { Name = "Members only" }.Save();

        (await Serve($"/gated/{work.Id}", SignedIn())).Should().Be("private");
        (await Serve($"/gated/{work.Id}", reader: null)).Should().BeEmpty();
    }

    private async Task<string> Serve(string path, ClaimsPrincipal? reader)
    {
        var app = new ApplicationBuilder(_host.Services);
        app.UseOpenGraphCards();
        var request = Navigation(path, reader);
        request.HttpContext.Response.Body = new MemoryStream();
        await app.Build()(request.HttpContext);
        return request.HttpContext.Response.Headers.CacheControl.ToString();
    }
}
