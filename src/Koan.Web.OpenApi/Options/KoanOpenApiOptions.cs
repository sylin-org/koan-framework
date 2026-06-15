namespace Koan.Web.OpenApi.Options;

/// <summary>
/// Options controlling Koan's built-in OpenAPI document exposure.
/// </summary>
public sealed class KoanOpenApiOptions
{
    /// <summary>
    /// Configuration section name for binding (Koan:OpenApi).
    /// </summary>
    public const string ConfigurationSection = "Koan:OpenApi";

    /// <summary>
    /// Default document name emitted by ASP.NET Core when no override is provided.
    /// </summary>
    public const string DefaultDocumentName = "v1";

    /// <summary>
    /// Default route pattern for the OpenAPI endpoint.
    /// </summary>
    public const string DefaultRoutePattern = "/openapi/{documentName}.json";

    /// <summary>
    /// When set, toggles OpenAPI exposure on/off.
    /// </summary>
    public bool? Enabled { get; set; }

    /// <summary>
    /// When set, toggles the Swagger UI on/off. Null = default true outside Production
    /// (off in Production unless explicitly enabled). Config key: Koan:OpenApi:EnableUi.
    /// </summary>
    public bool? EnableUi { get; set; }

    /// <summary>
    /// Route pattern used when mapping <c>MapOpenApi</c>.
    /// </summary>
    public string RoutePattern { get; set; } = DefaultRoutePattern;
}
