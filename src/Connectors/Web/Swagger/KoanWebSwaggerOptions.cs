namespace Koan.Web.Connector.Swagger;

public sealed class KoanWebSwaggerOptions
{
    public bool? Enabled { get; set; }
    public string RoutePrefix { get; set; } = "swagger";
    public bool IncludeXmlComments { get; set; } = true;
    public bool RequireAuthOutsideDevelopment { get; set; } = true;
    public bool DocumentHeaders { get; set; } = true;
}

