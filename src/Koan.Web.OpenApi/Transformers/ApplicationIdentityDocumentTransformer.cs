using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core;
using Koan.Core.Hosting.App;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Koan.Web.OpenApi.Transformers;

/// <summary>
/// Applies Koan application identity metadata to the generated OpenAPI document.
/// </summary>
internal sealed class ApplicationIdentityDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);

        var identity = ResolveIdentity(context.ApplicationServices);

        document.Info ??= new OpenApiInfo();
        document.Info.Title = string.IsNullOrWhiteSpace(identity.Name) ? "Koan API" : identity.Name;
        if (!string.IsNullOrWhiteSpace(identity.Description))
        {
            document.Info.Description = identity.Description;
        }

        if (!string.IsNullOrWhiteSpace(identity.ContactEmail) || !string.IsNullOrWhiteSpace(identity.SupportUrl))
        {
            document.Info.Contact ??= new OpenApiContact();
            if (!string.IsNullOrWhiteSpace(identity.ContactEmail))
            {
                document.Info.Contact.Email = identity.ContactEmail;
            }

            if (!string.IsNullOrWhiteSpace(identity.SupportUrl) && Uri.TryCreate(identity.SupportUrl, UriKind.Absolute, out var url))
            {
                document.Info.Contact.Url = url;
            }
        }

        if (!string.IsNullOrWhiteSpace(identity.Code))
        {
            var codeNode = JsonValue.Create(identity.Code);
            if (codeNode is not null)
            {
                document.AddExtension("x-koan-application-code", new JsonNodeExtension((JsonNode)codeNode));
            }
        }

        if (identity.Tags.Count > 0)
        {
            var tags = new JsonArray();
            foreach (var tag in identity.Tags)
            {
                var tagNode = JsonValue.Create(tag);
                if (tagNode is not null)
                {
                    tags.Add((JsonNode)tagNode);
                }
            }

            document.AddExtension("x-koan-tags", new JsonNodeExtension(tags));
        }

        return Task.CompletedTask;
    }

    private static ApplicationIdentitySnapshot ResolveIdentity(IServiceProvider? services)
    {
        if (services is not null)
        {
            KoanEnv.TryInitialize(services);
        }

        var identity = AppHost.Identity;
        return identity != ApplicationIdentitySnapshot.Empty
            ? identity
            : KoanEnv.CurrentSnapshot.Application;
    }
}
