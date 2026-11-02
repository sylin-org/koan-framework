using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Mcp.CustomTools;
using Koan.Web.Attributes;
using Microsoft.AspNetCore.Authorization;
using Koan.Web.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http;
using System.Reflection;
using Xunit;

namespace Koan.Mcp.Verbs.Tests;

/// <summary>
/// ARCH-0141: a tool that could not work stops the host at start, with the correction, and never fails a call. These controllers are
/// nested, which MVC does not discover by itself, so each host holds only the one it names.
/// </summary>
public sealed class ToolsThatCannotStartSpec
{
    public sealed class FirstOfOneName : ControllerBase
    {
        [HttpPost("/same/name")]
        [Tool]
        public IActionResult Write() => Ok();
    }

    public sealed class SecondOfOneName : ControllerBase
    {
        [HttpPost("/same/{id}/name")]
        [Tool]
        public IActionResult Write(string id) => Ok(id);
    }

    public sealed class Particled : ControllerBase
    {
        [HttpPost("/same/name")]
        [Tool]
        public IActionResult Write() => Ok();

        [HttpPost("/same/{id}/name")]
        [Tool(particle: "writeAgain")]
        public IActionResult Again(string id) => Ok(id);
    }

    public sealed class NamedLikeAnEntityTool : ControllerBase
    {
        [HttpPost("/x")]
        [Tool(Name = "not.a.good.name")]
        public IActionResult Write() => Ok();
    }

    public sealed class FileTaking : ControllerBase
    {
        [HttpPost("/upload")]
        [Tool]
        public IActionResult Write(IFormFile file) => Ok(file.Length);
    }

    public sealed class ByOneScheme : ControllerBase
    {
        [HttpPost("/schemed")]
        [Tool, Authorize(AuthenticationSchemes = "Elsewhere")]
        public IActionResult Write() => Ok();
    }

    public sealed record Nested(Nested.Inner Deep)
    {
        public sealed record Inner(string? Name);
    }

    public sealed class NestedInQuery : ControllerBase
    {
        [HttpGet("/nested")]
        [Tool]
        public IActionResult Read([FromQuery] Nested filter) => Ok();
    }

    public sealed class Unrouted : ControllerBase
    {
        [Tool]
        public IActionResult Write() => Ok();
    }

    private sealed class Only(params Type[] controllers) : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            feature.Controllers.Clear();
            foreach (var controller in controllers) feature.Controllers.Add(controller.GetTypeInfo());
        }
    }

    private static async Task Start(params Type[] controllers)
    {
        var path = Path.Combine(Path.GetTempPath(), $"koan-verbs-start-{Guid.CreateVersion7():n}.db");
        using var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
                ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={path};Pooling=False",
                ["Koan:Mcp:EnableStdioTransport"] = "false",
            }))
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.UseEnvironment("Development");
                web.ConfigureServices(services =>
                {
                    AppHost.Current = null;
                    services.AddKoan();
                    services.AddKoanWeb();
                    services.AddControllers().ConfigureApplicationPartManager(manager => manager.FeatureProviders.Add(new Only(controllers)));
                });
                web.Configure(_ => { });
            })
            .Build();
        try
        {
            await host.StartAsync(TestContext.Current.CancellationToken);
            await host.StopAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            foreach (var file in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public async Task Two_tools_of_one_name_stop_the_host_and_a_particle_answers_it()
    {
        var stopped = await Assert.ThrowsAsync<InvalidOperationException>(() => Start(typeof(FirstOfOneName), typeof(SecondOfOneName)));
        stopped.Message.Should().Contain("same_name_post").And.Contain("particle");

        await Start(typeof(Particled));
    }

    [Theory]
    [InlineData(typeof(NamedLikeAnEntityTool), "not.a.good.name")]
    [InlineData(typeof(FileTaking), "file")]
    [InlineData(typeof(Unrouted), "attribute route")]
    [InlineData(typeof(ByOneScheme), "Elsewhere")]
    [InlineData(typeof(NestedInQuery), "deep")]
    public async Task A_tool_that_cannot_be_one_stops_the_host_and_says_why(Type controller, string says)
    {
        var stopped = await Assert.ThrowsAnyAsync<Exception>(() => Start(controller));
        stopped.ToString().Should().Contain(says);
    }

    /// <summary>Not an [McpTool], or nothing in this assembly could boot: discovery stops the host at this.</summary>
    [Authorize]
    public static string AuthorizedVerb() => "";

    [Fact]
    public void Authorize_on_a_verb_with_no_route_is_refused_because_nothing_would_answer_to_it()
    {
        var registry = new McpCustomToolRegistry(Microsoft.Extensions.Logging.Abstractions.NullLogger<McpCustomToolRegistry>.Instance);
        var build = typeof(McpCustomToolRegistry).GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var refused = Assert.Throws<TargetInvocationException>(() => build.Invoke(registry, [typeof(ToolsThatCannotStartSpec).GetMethod(nameof(AuthorizedVerb)), new McpToolAttribute { Name = "authorized" }]));

        refused.InnerException.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain("[Access");
    }
}
