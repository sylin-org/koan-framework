namespace Koan.Web.OpenApi.Options;

/// <summary>
/// Options controlling Koan's built-in OpenAPI document exposure.
/// </summary>
public sealed class KoanOpenApiOptions
{
    /// <summary>
    /// Configuration section name for binding (Koan:OpenApi).
    /// </summary>
    public const string SectionPath = "Koan:OpenApi";

    /// <summary>
    /// Default document name emitted by ASP.NET Core when no override is provided.
    /// </summary>
    public const string DefaultDocumentName = "v1";

    /// <summary>
    /// Default route pattern for the OpenAPI endpoint.
    /// </summary>
    public const string DefaultRoutePattern = "/openapi/{documentName}.json";

    /// <summary>
    /// Default route prefix for the interactive OpenAPI UI.
    /// </summary>
    public const string DefaultUiRoute = "swagger";

    /// <summary>
    /// When set, toggles OpenAPI exposure on/off.
    /// </summary>
    public bool? Enabled { get; set; }

    /// <summary>
    /// When set, toggles the interactive OpenAPI UI. Null enables it only in Development.
    /// </summary>
    public bool? EnableUi { get; set; }

    /// <summary>
    /// Route pattern used when mapping <c>MapOpenApi</c>.
    /// </summary>
    public string RoutePattern { get; set; } = DefaultRoutePattern;

    /// <summary>
    /// Route prefix used by the interactive OpenAPI UI.
    /// </summary>
    public string UiRoute { get; set; } = DefaultUiRoute;

    /// <summary>
    /// Requires an authenticated caller when the UI is explicitly enabled outside Development.
    /// </summary>
    public bool RequireAuthenticationOutsideDevelopment { get; set; } = true;
}
