namespace Koan.AI.Models;

/// <summary>
/// Thrown when multiple adapters or source providers can handle a model ID
/// and the caller did not specify which one to use via the <c>to</c> parameter.
/// </summary>
public sealed class AmbiguousModelSourceException : InvalidOperationException
{
    public IReadOnlyList<string> AvailableAdapters { get; }

    public AmbiguousModelSourceException(string modelId, IReadOnlyList<string> adapters)
        : base($"Multiple adapters can handle '{modelId}': [{string.Join(", ", adapters)}]. " +
               $"Specify the target: Model.Pull(\"{modelId}\", to: \"{adapters[0]}\")")
    {
        AvailableAdapters = adapters;
    }
}
