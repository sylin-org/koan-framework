using Koan.Rag.Abstractions;

namespace Koan.Rag.Content.Strategies;

/// <summary>
/// Pre-determined interpretation strategies shipped with the framework.
/// Each strategy is an optimized prompt template for a known content type,
/// designed to extract the maximum meaningful information.
/// </summary>
internal static class BuiltInStrategies
{
    /// <summary>
    /// Resolve a pre-determined strategy by classification category.
    /// Returns null if no built-in strategy matches (triggers auto-generation).
    /// </summary>
    public static InterpretationStrategy? Match(ContentClassification classification)
    {
        // Try exact match first, then prefix match
        var category = classification.Category.ToLowerInvariant();

        return _strategies.GetValueOrDefault(category)
            ?? _strategies.FirstOrDefault(kv =>
                category.StartsWith(kv.Key, StringComparison.Ordinal)).Value;
    }

    private static readonly Dictionary<string, InterpretationStrategy> _strategies = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ["diagram/architecture"] = new()
        {
            Id = "diagram/architecture",
            MatchCategories = ["diagram/architecture", "diagram/system", "diagram/network"],
            Origin = StrategyOrigin.PreDetermined,
            InterpretationPrompt =
                """
                Analyze this architecture/system diagram in detail:

                1. COMPONENTS: List every component (services, databases, queues, gateways, caches, external systems). For each: name, role, and technology if identifiable.
                2. CONNECTIONS: Describe every connection between components. Direction, protocol/method if shown, and what data flows through each.
                3. DATA FLOWS: Trace the primary data paths from entry points to storage. Identify request/response patterns.
                4. FAILURE PATHS: Identify fallback paths, circuit breakers, retry arrows, dashed lines indicating degraded operation.
                5. BOUNDARIES: Note any groupings, zones, or boundary lines (security zones, VPCs, namespaces, deployment units).

                Present as structured text with clear headings. Use component names consistently.
                """,
            EnrichmentPrompt =
                """
                Based on your interpretation of this architecture diagram, identify:

                1. IMPLICIT CONSTRAINTS: What architectural decisions are being enforced by this design? (e.g., "all external traffic must enter through the gateway")
                2. SINGLE POINTS OF FAILURE: Components whose failure would cause cascading issues.
                3. SCALABILITY IMPLICATIONS: Which components appear to be stateless vs stateful? Where would bottlenecks form under load?
                4. SECURITY ASSUMPTIONS: What trust boundaries exist? Where is authentication/authorization enforced?
                """,
            RequiresEnrichment = true
        },

        ["diagram/sequence"] = new()
        {
            Id = "diagram/sequence",
            MatchCategories = ["diagram/sequence", "diagram/process", "diagram/flow"],
            Origin = StrategyOrigin.PreDetermined,
            InterpretationPrompt =
                """
                Analyze this sequence/process/flow diagram:

                1. ACTORS/PARTICIPANTS: List every actor, service, or system involved. Note their roles.
                2. STEPS: Describe each step in order. Include: initiator, action, receiver, and any data passed.
                3. DECISION POINTS: Identify any branching logic, conditions, or guards.
                4. ERROR PATHS: Note alternative flows, exception handling, timeout behaviors.
                5. RETURN VALUES: What does each call return? Note success vs failure responses.

                Present as a numbered sequence with clear actor labels.
                """,
            EnrichmentPrompt =
                """
                Based on this sequence diagram, identify:

                1. PRECONDITIONS: What must be true before this flow starts?
                2. POSTCONDITIONS: What state changes occur after completion?
                3. EDGE CASES: What happens if a step fails midway? Are there compensating transactions?
                4. ORDERING CONSTRAINTS: Which steps must be sequential vs could be parallel?
                """,
            RequiresEnrichment = true
        },

        ["table"] = new()
        {
            Id = "table",
            MatchCategories = ["table", "spreadsheet", "matrix"],
            Origin = StrategyOrigin.PreDetermined,
            InterpretationPrompt =
                """
                Extract and interpret this tabular data:

                1. STRUCTURE: Column headers, row headers (if any), and the table's dimensions.
                2. DATA: All cell values, preserving the relationship between headers and values. Format as key-value assertions (e.g., "Product X has price $50" not just "$50").
                3. RELATIONSHIPS: What relationships do the columns encode? (e.g., entity-attribute, time-series, comparison)
                4. DERIVED INSIGHTS: Any totals, averages, or summary rows. Trends visible in the data.

                Convert every row into a self-contained assertion that would be meaningful without seeing the table.
                """,
            EnrichmentPrompt =
                """
                Based on this table:

                1. OUTLIERS: Any values that stand out as unusual compared to the rest?
                2. TRENDS: Any patterns across rows or columns?
                3. MISSING DATA: Any gaps or null values that might be significant?
                4. IMPLICATIONS: What decisions or conclusions does this data support?
                """,
            RequiresEnrichment = true
        },

        ["chart"] = new()
        {
            Id = "chart",
            MatchCategories = ["chart", "graph/data", "visualization"],
            Origin = StrategyOrigin.PreDetermined,
            InterpretationPrompt =
                """
                Analyze this chart/graph visualization:

                1. TYPE: What kind of chart (bar, line, pie, scatter, etc.)?
                2. AXES: What do the X and Y axes represent? Units? Scale?
                3. DATA SERIES: Each series name, its values, and the trend it shows.
                4. KEY VALUES: Maximum, minimum, inflection points, crossover points.
                5. ANNOTATIONS: Any labels, callouts, or highlighted regions.

                Translate the visual data into precise textual statements.
                """,
            EnrichmentPrompt =
                """
                Based on this chart:

                1. CORRELATIONS: Any relationships between data series?
                2. ANOMALIES: Any data points that break the expected pattern?
                3. PROJECTIONS: If the trend continues, what would you expect?
                4. CONTEXT: What story does this data tell?
                """,
            RequiresEnrichment = true
        },

        ["form"] = new()
        {
            Id = "form",
            MatchCategories = ["form", "document/scan", "document/form"],
            Origin = StrategyOrigin.PreDetermined,
            InterpretationPrompt =
                """
                Extract all information from this form/document:

                1. FIELDS: Every field label and its value. Include empty fields with "[blank]".
                2. STRUCTURE: Sections, groups, or categories of fields.
                3. SIGNATURES/DATES: Any signatures, dates, stamps, or approval marks.
                4. FINE PRINT: Any notes, disclaimers, or conditions in small text.

                Format as structured key-value pairs grouped by section.
                """,
            EnrichmentPrompt = null,
            RequiresEnrichment = false
        },

        ["code"] = new()
        {
            Id = "code",
            MatchCategories = ["code", "code/snippet"],
            Origin = StrategyOrigin.PreDetermined,
            InterpretationPrompt =
                """
                Analyze this code:

                1. PURPOSE: What does this code do? Summarize in one paragraph.
                2. LANGUAGE: Programming language and any frameworks/libraries used.
                3. KEY COMPONENTS: Classes, functions, methods — what each does.
                4. DEPENDENCIES: External libraries, services, or APIs referenced.
                5. PATTERNS: Design patterns, idioms, or architectural patterns used.

                Focus on the intent and behavior, not line-by-line description.
                """,
            EnrichmentPrompt = null,
            RequiresEnrichment = false
        },

        ["photograph"] = new()
        {
            Id = "photograph",
            MatchCategories = ["photograph", "photo", "image/photo"],
            Origin = StrategyOrigin.PreDetermined,
            InterpretationPrompt =
                """
                Fully describe this photograph:

                1. SUBJECT: What is the primary subject? Describe its nature, attributes, condition.
                2. CONTEXT: Setting, environment, background. What does the context tell us?
                3. DETAILS: Notable details — text visible, labels, markings, colors, materials.
                4. RELATIONSHIPS: If multiple subjects, how do they relate spatially and contextually?
                5. CLASSIFICATION: What category does this belong to? (product, document, scene, person, equipment, etc.)
                """,
            EnrichmentPrompt =
                """
                Based on this photograph:

                1. COMPARABLE ITEMS: What is this similar to?
                2. CONDITION: What is the condition/state of the subject?
                3. NOTABLE FEATURES: What distinguishes this from similar items?
                """,
            RequiresEnrichment = true
        },

        ["screenshot"] = new()
        {
            Id = "screenshot",
            MatchCategories = ["screenshot", "ui", "interface"],
            Origin = StrategyOrigin.PreDetermined,
            InterpretationPrompt =
                """
                Analyze this UI screenshot:

                1. SCREEN: What application/page is this? Title, URL if visible.
                2. COMPONENTS: List all UI elements (buttons, forms, menus, panels, modals).
                3. STATE: What state is the UI in? (loading, error, success, editing, viewing)
                4. DATA: Any data displayed — table contents, form values, messages.
                5. NAVIGATION: What actions are available from this screen?

                Focus on what a user sees and can do.
                """,
            EnrichmentPrompt = null,
            RequiresEnrichment = false
        }
    };
}
