namespace Koan.Mcp.Resources;

/// <summary>
/// P1.2 — an MCP resource descriptor (the <c>resources/list</c> item). A resource is a named, readable
/// document addressed by a <c>uri</c> (e.g. <c>koan://entities</c>) — the introspection surface an agent
/// pulls to learn what the app projects, distinct from the executable tools.
/// </summary>
public sealed record McpResourceDescriptor(string Uri, string Name, string? Description, string MimeType);

/// <summary>P1.2 — the body of a resource (the <c>resources/read</c> result for a single uri).</summary>
public sealed record McpResourceContents(string Uri, string MimeType, string Text);
