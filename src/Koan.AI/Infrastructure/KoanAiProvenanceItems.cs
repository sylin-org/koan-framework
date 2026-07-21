using Koan.Core.Hosting.Bootstrap;

namespace Koan.AI.Infrastructure;

internal static class KoanAiProvenanceItems
{
    internal static readonly ProvenanceItem AutoDiscoveryEnabled = new(
        "AiAutoDiscoveryEnabled",
        "Auto-Discovery Enabled",
        "Controls whether Koan.AI scans for adapters during boot.");

    internal static readonly ProvenanceItem AllowDiscoveryOutsideDevelopment = new(
        "AiAllowDiscoveryOutsideDevelopment",
        "Allow Discovery Outside Development",
        "Permits adapter discovery when the environment is not Development.");

    internal static readonly ProvenanceItem DefaultRoutingPolicy = new(
        "AiDefaultRoutingPolicy",
        "Default Routing Policy",
        "Pipeline policy used when no explicit AI route is supplied.");

    internal static readonly ProvenanceItem ConfiguredSources = new(
        "AiConfiguredSources",
        "Configured Sources",
        "List of AI sources explicitly declared under Koan:Ai:Sources.");

    internal static readonly ProvenanceItem ActiveRecipe = new(
        "AiActiveRecipe",
        "Active Recipe",
        "Named recipe providing per-category model bindings (Koan:Ai:ActiveRecipe).");

    internal static readonly ProvenanceItem CategoryRouting = new(
        "AiCategoryRouting",
        "Category Routing",
        "Per-category (Chat, Embed, Ocr) source and model configuration.");

    internal static readonly ProvenanceItem AdapterRoster = new(
        "AiAdapterRoster",
        "Adapter Roster",
        "Live AI adapters with capability flags and provisioning support.",
        false,
        false,
        null,
        null,
        null,
        new[] { "Koan.AI" });

    internal static readonly ProvenanceItem SourceMemberStatus = new(
        "AiSourceMemberStatus",
        "Source & Member Health",
        "Aggregated AI source health and member availability snapshot.",
        false,
        false,
        null,
        null,
        null,
        new[] { "Koan.AI" });
}
