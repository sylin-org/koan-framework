using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core.Hosting.Bootstrap;
using Koan.Mcp.CustomTools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Mcp.CustomTools.Tests;

/// <summary>Sample [McpTool] verbs used by the custom-tool tests.</summary>
public static class SampleTools
{
    [McpTool(Name = "echo_upper", Description = "Uppercases the input.")]
    public static string EchoUpper(string text, string? suffix = null)
        => (text + (suffix ?? string.Empty)).ToUpperInvariant();

    [McpTool(Name = "service_greeting", Description = "Greets using an injected service + the call token.")]
    public static async Task<string> ServiceGreeting(string name, IServiceProvider services, CancellationToken ct)
    {
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
        var greeter = services.GetService<IGreeter>();
        return greeter is null ? $"hi {name}" : greeter.Greet(name);
    }
}

public interface IGreeter { string Greet(string name); }
public sealed class Greeter : IGreeter { public string Greet(string name) => $"HELLO {name}!"; }

/// <summary>
/// Verifies the Koan.Mcp custom-verb tool capability: a static method decorated with [McpTool] is
/// discovered with a generated input schema, and is invoked with argument binding plus injection of
/// IServiceProvider / CancellationToken.
/// </summary>
public sealed class McpCustomToolTests
{
    private static McpCustomToolRegistry BuildRegistry()
    {
        // The registry discovers across AssemblyCache (populated during Koan boot); seed this test
        // assembly so its [McpTool] sample methods are visible without a full host.
        AssemblyCache.Instance.AddAssembly(typeof(SampleTools).Assembly);
        return new McpCustomToolRegistry(NullLogger<McpCustomToolRegistry>.Instance);
    }

    [Fact]
    public void Discovers_McpTool_methods_with_generated_schema()
    {
        var registry = BuildRegistry();

        registry.TryGet("echo_upper", out var tool).Should().BeTrue();
        tool.Description.Should().Be("Uppercases the input.");

        var props = tool.InputSchema["properties"]!;
        props["text"].Should().NotBeNull("'text' is a bound argument");
        props["suffix"].Should().NotBeNull("'suffix' is a bound argument");

        var required = ((JArray)tool.InputSchema["required"]!).Select(t => t.Value<string>()).ToArray();
        required.Should().Contain("text", "it has no default value");
        required.Should().NotContain("suffix", "it has a default value");

        // Injected parameters never appear in the input schema.
        registry.TryGet("service_greeting", out var greeting).Should().BeTrue();
        var greetingProps = greeting.InputSchema["properties"]!;
        greetingProps["name"].Should().NotBeNull();
        greetingProps["services"].Should().BeNull("IServiceProvider is injected, not an argument");
        greetingProps["ct"].Should().BeNull("CancellationToken is injected, not an argument");
    }

    [Fact]
    public async Task Invokes_with_argument_binding()
    {
        var registry = BuildRegistry();
        var invoker = new McpCustomToolInvoker();
        registry.TryGet("echo_upper", out var tool).Should().BeTrue();

        var result = await invoker.Invoke(
            tool,
            new JObject { ["text"] = "hello", ["suffix"] = "!" },
            new ServiceCollection().BuildServiceProvider(),
            CancellationToken.None);

        result.Value<string>().Should().Be("HELLO!");
    }

    [Fact]
    public async Task Injects_service_provider_and_token()
    {
        var registry = BuildRegistry();
        var invoker = new McpCustomToolInvoker();
        registry.TryGet("service_greeting", out var tool).Should().BeTrue();

        var services = new ServiceCollection().AddSingleton<IGreeter, Greeter>().BuildServiceProvider();
        var result = await invoker.Invoke(tool, new JObject { ["name"] = "world" }, services, CancellationToken.None);

        result.Value<string>().Should().Be("HELLO world!");
    }
}
