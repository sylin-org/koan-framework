# Koan.Mcp

## Contract
- **Purpose**: Implement a Model Context Protocol (MCP) host for Koan modules, exposing entities, tools, and diagnostics to AI-powered agents.
- **Primary inputs**: `McpEntityAttribute`-decorated types, entity registries built from `McpEntityRegistration`, Koan adapters describing available capabilities.
- **Outputs**: MCP descriptors composed via `DescriptorMapper`, hosted endpoints through the Koan hosting layer, and tool definitions consumable by MCP clients.
- **Failure modes**: Missing entity annotations, unsupported transport modes, or MCP schema mismatches when serializing descriptors.
- **Success criteria**: MCP clients can enumerate Koan entities/tools, execute actions through the protocol, and receive diagnostics in the expected schema.

## Quick start
```csharp
using Koan.Mcp;
using Koan.Mcp.Hosting;

[McpEntity("orders", DisplayName = "Orders")]
public sealed class OrderEntity : McpEntityRegistration
{
    public override Task DescribeAsync(IMcpDescriptorBuilder builder, CancellationToken ct)
    {
        builder.WithListHandler(async request =>
        {
            var page = await Order.Page(request.PageNumber, request.PageSize, ct);
            return builder.Result(page.Items, page.Total);
        });
        return Task.CompletedTask;
    }
}

public sealed class McpAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "MCP";

    public void Initialize(IServiceCollection services)
    {
        services.AddMcpHost();
        services.RegisterMcpEntity<OrderEntity>();
    }

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
        => report.AddNote("MCP host enabled");
}
```
- Annotate registrations with `McpEntityAttribute` and implement `DescribeAsync` to wire Koan entity operations (`Order.Page(...)`, `Order.QueryStream(...)`).
- Register the MCP host and entity registrations via `IKoanAutoRegistrar` for zero-boilerplate hosting.

## Configuration
- Choose transport via `McpOptions.TransportMode` (`Http`, `Stdio`, etc.) depending on deployment.
- Enable diagnostics by registering `McpDiagnosticsService` and injecting telemetry exporters.
- For large entity datasets, prefer streaming responses using `builder.StreamAsync(...)` to avoid payload bloat.

## Edge cases
- Schema drift between agent and host: update descriptors or bump protocol version to stay compatible.
- Long-running tool executions: emit progress via diagnostics channel to keep agents responsive.
- Authentication: integrate Koan Web Auth middleware if exposing MCP over HTTP.
- Entity deletion: ensure descriptors mark unsupported operations when adapters lack delete semantics.

## Related packages
- `Koan.Core` – DI patterns, boot reporting, and environment helpers used by MCP host.
- `Koan.Data.Core` – provides entity paging/streaming used in MCP handlers.
- `Koan.Core.Adapters` – adapter discovery feeding into MCP tool projection.

## Reference
- `McpEntityRegistration` – base class for exposing Koan entities over MCP.
- `DescriptorMapper` – builds MCP-compliant descriptors from adapter metadata.
- `McpToolDefinition` – describe tools triggered by agents.
