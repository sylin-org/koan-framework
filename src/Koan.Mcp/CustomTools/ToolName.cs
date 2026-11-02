using System.Text.RegularExpressions;

namespace Koan.Mcp.CustomTools;

/// <summary>
/// The one rule for a tool's name, whichever door made it (ARCH-0142): lower-case words joined by underscores, at most 64 characters, which every client takes.
/// MCP allows a dot, and Claude's Messages API refuses one (<c>^[a-zA-Z0-9_-]{1,128}$</c>); OpenAI's function names stop at 64.
/// </summary>
internal static partial class ToolName
{
    public const string Rule = "lower-case words joined by underscores, at most 64 characters";

    [GeneratedRegex("^[a-z0-9_]{1,64}$")]
    private static partial Regex Good();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotAWord();

    [GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])")]
    private static partial Regex WordBoundary();

    public static bool IsGood(string name) => Good().IsMatch(name);

    /// <summary>The parts as one name: <c>getById</c>, <c>TodoItem</c> and <c>get-by-id</c> are all the words they read as.</summary>
    public static string Of(params IEnumerable<string?> parts)
        => string.Join('_', parts.Where(static part => !string.IsNullOrWhiteSpace(part)).Select(static part => Words(part!)).Where(static word => word.Length > 0));

    private static string Words(string text)
        => NotAWord().Replace(WordBoundary().Replace(text, "_").ToLowerInvariant(), "_").Trim('_');
}
