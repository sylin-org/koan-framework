namespace Koan.Data.Abstractions.Filtering;

/// <summary>
/// Thrown when a JSON filter payload is malformed or uses an operator incorrectly (unknown
/// operator, non-array <c>$in</c>, <c>$size</c> on a scalar field, etc.). Surfaces as
/// <c>400 Bad Request</c> in the web layer — fail loud, never silently mis-filter.
/// </summary>
public sealed class FilterParseException : Exception
{
    public FilterParseException(string message, Exception? inner = null) : base(message, inner) { }
}
