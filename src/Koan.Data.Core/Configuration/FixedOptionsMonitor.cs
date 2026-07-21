using System.ComponentModel;
using Microsoft.Extensions.Options;

namespace Koan.Data.Core.Configuration;

/// <summary>
/// Adapts one immutable, already-resolved route value to the options-monitor contract expected by
/// provider connection and readiness mechanics. A routed source is compiled once and never reloads.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class FixedOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    where T : class
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
