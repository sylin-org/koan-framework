namespace Koan.Data.Abstractions.Sources;

/// <summary>A stable, corrective rejection raised before an operation exceeds its source ceiling.</summary>
public sealed class DataSourcePolicyException : InvalidOperationException
{
    public const string PolicyDeniedCode = "data.source.policy-denied";
    public const string UnknownEffectCode = "data.source.effect-unknown";

    public DataSourcePolicyException(
        string source,
        string operation,
        DataOperationEffect effect,
        StorageLifecycle storageLifecycle,
        DataSourceAccess access,
        string code,
        string correction)
        : base(
            $"Source '{source}' rejected '{operation}' ({effect}) under " +
            $"StorageLifecycle={storageLifecycle}, Access={access}. {correction}")
    {
        SourceName = source;
        Operation = operation;
        Effect = effect;
        StorageLifecycle = storageLifecycle;
        Access = access;
        Code = code;
        Correction = correction;
    }

    public string SourceName { get; }
    public string Operation { get; }
    public DataOperationEffect Effect { get; }
    public StorageLifecycle StorageLifecycle { get; }
    public DataSourceAccess Access { get; }
    public string Code { get; }
    public string Correction { get; }
}
