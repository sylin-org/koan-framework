namespace Koan.Data.Abstractions;

/// <summary>Immutable native SQL/SQL++ payload for one registered source operation.</summary>
public sealed record SqlOperationBinding : IDataOperationBinding
{
    public SqlOperationBinding(string commandText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandText);
        CommandText = commandText;
    }

    public string CommandText { get; }
    public string Kind => "sql";
    public OperationBindingEffectProof EffectProof => OperationBindingEffectProof.Opaque;
}
