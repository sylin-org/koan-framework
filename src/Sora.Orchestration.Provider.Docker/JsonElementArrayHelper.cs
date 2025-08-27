namespace Sora.Orchestration.Provider.Docker;

internal static class JsonElementArrayHelper
{
    // Helper to adapt a single element into an array-like iteration without allocations elsewhere
    public static System.Text.Json.JsonElement ToJsonArrayElement(this System.Text.Json.JsonElement[] elements)
    {
        // Build a JSON array string from the single element(s) and parse once
        using var doc = System.Text.Json.JsonDocument.Parse("[" + string.Join(',', elements.Select(e => e.GetRawText())) + "]");
        return doc.RootElement;
    }
}