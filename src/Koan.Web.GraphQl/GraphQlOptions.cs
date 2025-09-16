using System.ComponentModel.DataAnnotations;

namespace Koan.Web.GraphQl;

public sealed class GraphQlOptions
{
    // Enable/disable GraphQL endpoints
    public bool? Enabled { get; set; }

    // Request path for GraphQL endpoint
    [Required]
    [RegularExpression("^/.+", ErrorMessage = "GraphQl path must start with '/'.")]
    public string Path { get; set; } = "/graphql";

    // Allow toggling debug behavior without headers
    public bool Debug { get; set; }
}
