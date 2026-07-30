namespace Koan.Data.Abstractions;

/// <summary>Provider/family-owned immutable native payload selected at composition.</summary>
public interface IDataOperationBinding
{
    string Kind { get; }
    OperationBindingEffectProof EffectProof { get; }
}
