namespace Koan.Core.Infrastructure;

// Centralized constants across the Koan platform
public static class Constants
{
    public static class Configuration
    {
        public static class Koan
        {
            public const string AllowMagicInProduction = "Koan:AllowMagicInProduction";
        }

        public static class Observability
        {
            public const string Section = "Koan:Observability";
        }

        public static class Otel
        {
            public static class Exporter
            {
                public static class Otlp
                {
                    public const string Endpoint = "OTEL:EXPORTER:OTLP:ENDPOINT";
                    public const string Headers = "OTEL:EXPORTER:OTLP:HEADERS";
                }
            }
        }

        public static class Env
        {
            public const string DotnetEnvironment = "DOTNET:ENVIRONMENT";
            public const string AspNetCoreEnvironment = "ASPNETCORE:ENVIRONMENT";
            public const string DotnetRunningInContainer = "DOTNET:RUNNING:IN:CONTAINER";
            public const string KubernetesServiceHost = "KUBERNETES:SERVICE:HOST";
            public const string Ci = "CI";
            public const string TfBuild = "TF:BUILD";
        }


    }
}
