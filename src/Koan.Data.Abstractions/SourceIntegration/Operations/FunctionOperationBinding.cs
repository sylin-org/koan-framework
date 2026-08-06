namespace Koan.Data.Abstractions;

/// <summary>
/// Immutable binding for a provider-native named function. Keys are explicit because clustered key/value providers
/// require the complete key set at dispatch; a key beginning with <c>@</c> resolves from the operation parameter of
/// the same name.
/// </summary>
public sealed record FunctionOperationBinding : IDataOperationBinding
{
    public FunctionOperationBinding(string name, params string[] keys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Function keys cannot be blank.", nameof(keys));
        Name = name.Trim();
        Keys = Array.AsReadOnly(keys.Select(static key => key.Trim()).ToArray());
    }

    public string Name { get; }
    public IReadOnlyList<string> Keys { get; }
    public string Kind => "function";
    public OperationBindingEffectProof EffectProof => OperationBindingEffectProof.ValidatedRead;
}
