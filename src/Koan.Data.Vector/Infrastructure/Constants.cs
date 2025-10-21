namespace Koan.Data.Vector.Infrastructure;

internal static class Constants
{
    internal static class Configuration
    {
        public const string DefaultsSection = "Koan:Data:VectorDefaults";
        public const string WorkflowsSection = "Koan:Data:Vector";

        internal static class Keys
        {
            public const string DefaultProvider = "Koan:Data:VectorDefaults:DefaultProvider";
            public const string EnableWorkflows = "Koan:Data:Vector:EnableWorkflows";
        }
    }

    internal static class Workflows
    {
        public const string Profiles = "Koan:Data:Vector:Profiles";
        public const string DefaultProfileName = "default";
    }
}
