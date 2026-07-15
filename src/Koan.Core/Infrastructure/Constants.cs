namespace Koan.Core.Infrastructure;

// Centralized constants across the Koan platform
public static class Constants
{
    public static class Diagnostics
    {
        public const int FactSchemaVersion = 1;

        public static class Codes
        {
            public const string ModuleActivated = "koan.bootstrap.module.activated";
            public const string ModuleRejected = "koan.bootstrap.module.rejected";
            public const string CollectionFailed = "koan.diagnostics.collection.failed";
            public const string ElectionSelected = "koan.composition.election.selected";
            public const string LockfileMatched = "koan.composition.lockfile.matched";
            public const string LockfileDrifted = "koan.composition.lockfile.drifted";
            public const string LockfileMissing = "koan.composition.lockfile.missing";
            public const string ServiceDiscovery = "koan.discovery.service";
        }

        public static class Reasons
        {
            public const string InitializerCompleted = "initializer-completed";
            public const string InitializerFailed = "initializer-failed";
            public const string ReporterFailed = "reporter-failed";
            public const string LockfileMatched = "lockfile-matched";
            public const string LockfileDrifted = "lockfile-drifted";
            public const string LockfileMissing = "lockfile-missing";
            public const string DiscoverySelected = "discovery-selected";
            public const string DiscoveryFailed = "discovery-failed";
            public const string DiscoveryAdapterMissing = "discovery-adapter-missing";
        }
    }

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
