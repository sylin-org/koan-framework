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

        public static class Orchestration
        {
            public const string Section = "Koan:Orchestration";
            public const string ForceOrchestrationMode = "Koan:Orchestration:ForceOrchestrationMode";
            public const string EnableSelfOrchestration = "Koan:Orchestration:EnableSelfOrchestration";
            public const string ValidateNetworking = "Koan:Orchestration:ValidateNetworking";
            public const string NetworkValidationTimeoutMs = "Koan:Orchestration:NetworkValidationTimeoutMs";
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
            public const string KubernetesServicePort = "KUBERNETES:SERVICE:PORT";
            public const string Ci = "CI";
            public const string TfBuild = "TF:BUILD";

            // Docker Compose Environment Variables
            public const string ComposeProjectName = "COMPOSE:PROJECT:NAME";
            public const string ComposeService = "COMPOSE:SERVICE";
            public const string ComposeContainerName = "COMPOSE:CONTAINER:NAME";

            // Aspire Environment Variables
            public const string AspireResourceName = "ASPIRE:RESOURCE:NAME";
            public const string AspireUrls = "ASPIRE:URLS";
            public const string AspireAllowUnsecuredTransport = "ASPIRE:ALLOW:UNSECURED:TRANSPORT";

            // Koan Orchestration Session Management
            public const string KoanSessionId = "KOAN:SESSION:ID";
        }


    }
}
