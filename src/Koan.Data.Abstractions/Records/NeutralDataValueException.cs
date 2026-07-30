namespace Koan.Data.Abstractions;

public sealed class NeutralDataValueException : InvalidOperationException
{
    public NeutralDataValueException(Type valueType)
        : base(
            $"Value type '{valueType.FullName}' is not in Koan's neutral record algebra. " +
            "Convert it to a neutral scalar, DataObject, or DataArray, or keep it behind a provider-native surface.")
        => ValueType = valueType;

    public Type ValueType { get; }
}
