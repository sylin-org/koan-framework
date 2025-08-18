namespace Sora.Web.Swagger;

public sealed class SoraWebSwaggerOptions
{
    public bool? Enabled { get; set; }
    public string RoutePrefix { get; set; } = "swagger";
    public bool IncludeXmlComments { get; set; } = true;
    public bool RequireAuthOutsideDevelopment { get; set; } = true;
    public bool DocumentHeaders { get; set; } = true;
}
