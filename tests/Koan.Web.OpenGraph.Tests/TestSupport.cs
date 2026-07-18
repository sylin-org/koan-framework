using Koan.Data.Core.Model;
using Microsoft.Extensions.Options;

namespace Koan.Web.OpenGraph.Tests;

/// <summary>A domain-agnostic entity used as a card source in tests.</summary>
public sealed class TestWork : Entity<TestWork>
{
    public string Name { get; set; } = "";
    public string Summary { get; set; } = "";
    public string? CoverMediaId { get; set; }
}

/// <summary>A second entity, to exercise the cross-type For&lt;U&gt; chain.</summary>
public sealed class TestArticle : Entity<TestArticle>
{
    public string Title { get; set; } = "";
    public string Excerpt { get; set; } = "";
}

/// <summary>Minimal <see cref="IOptionsMonitor{T}"/> over a fixed value for unit tests.</summary>
internal sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
{
    public TestOptionsMonitor(T value) => CurrentValue = value;

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
