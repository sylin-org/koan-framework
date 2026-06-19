using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Mcp.Options;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Resources;

/// <summary>
/// AN8 (docs/assessment/09 §11.1) — the <c>koan://self</c> self-introduction: the projector's per-grant
/// output rendered in TWO faces in one resource. A first-person <c>prose</c> greeting (the menu, written
/// from the app identity + the verbs visible to THIS caller) AND the <c>structured</c> contract beneath it
/// (exact entities/verbs). Prose is the greeting; structured is the contract — prose is NEVER the only
/// form (it is lossy; exact verb names/schemas don't survive a friendly sentence). The menu reshapes per
/// grant and is authored by nobody — rename the app → it updates; a walled entity → it vanishes.
///
/// App identity is <c>[KoanApp]</c> → <see cref="KoanEnv.CurrentSnapshot"/>.Application (no
/// <c>[McpApplication]</c>, 09 §13). The Door's "one step further" line + admin-tier semantics layer on
/// with AN5 (the Door projection).
/// </summary>
public sealed class SelfResourceProvider : IMcpResourceProvider
{
    public const string ResourceUri = "koan://self";

    private readonly McpEntityRegistry _registry;
    private readonly IOptions<McpServerOptions> _options;

    public SelfResourceProvider(McpEntityRegistry registry, IOptions<McpServerOptions> options)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public IEnumerable<McpResourceDescriptor> List(ClaimsPrincipal? user)
    {
        yield return new McpResourceDescriptor(
            ResourceUri,
            "About this application",
            "A first-person introduction to what this application is and what you can do here, right now.",
            "application/json");
    }

    public McpResourceContents? Read(string uri, ClaimsPrincipal? user)
    {
        if (!string.Equals(uri, ResourceUri, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var app = KoanEnv.CurrentSnapshot.Application;
        var visible = EntityProjection.Visible(_registry, _options.Value, user);

        var entities = new JArray(visible.Select(v => new JObject
        {
            ["name"] = v.Registration.DisplayName,
            ["description"] = v.Registration.Attribute.Description,
            ["verbs"] = new JArray(v.Verbs.Select(tool => new JObject
            {
                ["name"] = tool.Name,
                ["operation"] = tool.Operation.ToString(),
                ["isMutation"] = tool.IsMutation
            }))
        }));

        var document = new JObject
        {
            ["prose"] = BuildProse(app, visible),
            ["identity"] = new JObject
            {
                ["name"] = app.Name,
                ["code"] = app.Code,
                ["description"] = app.Description,
                ["contactEmail"] = app.ContactEmail,
                ["supportUrl"] = app.SupportUrl,
                ["tags"] = new JArray(app.Tags)
            },
            ["entities"] = entities
        };

        return new McpResourceContents(ResourceUri, "application/json", document.ToString(Formatting.None));
    }

    private static string BuildProse(
        ApplicationIdentitySnapshot app,
        IReadOnlyList<(McpEntityRegistration Registration, IReadOnlyList<McpToolDefinition> Verbs)> visible)
    {
        var sb = new StringBuilder();
        sb.Append("I'm ").Append(app.Name).Append('.');
        if (!string.IsNullOrWhiteSpace(app.Description))
        {
            sb.Append(' ').Append(app.Description.TrimEnd('.')).Append('.');
        }

        if (visible.Count == 0)
        {
            sb.Append(" There's nothing here you can use yet.");
            return sb.ToString();
        }

        sb.Append(" You can work with: ")
          .Append(string.Join(", ", visible.Select(v => v.Registration.DisplayName)))
          .Append('.');

        foreach (var (registration, verbs) in visible)
        {
            var canModify = verbs.Any(tool => tool.IsMutation);
            sb.Append(" For ").Append(registration.DisplayName).Append(" you can ")
              .Append(canModify ? "read and modify" : "read")
              .Append(" records.");
        }

        return sb.ToString();
    }
}
