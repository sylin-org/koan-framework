namespace Koan.Web.Swagger.Infrastructure;

public static class Constants
{
    public static class Configuration
    {
        public const string Section = "Koan:Web:Swagger";
        public const string Enabled = "Koan:Web:Swagger:Enabled";
        public static class Keys
        {
            public const string Enabled = nameof(Enabled);
            public const string RoutePrefix = nameof(RoutePrefix);
            public const string IncludeXmlComments = nameof(IncludeXmlComments);
            public const string RequireAuthOutsideDevelopment = nameof(RequireAuthOutsideDevelopment);
        }
    }
}
