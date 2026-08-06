using Koan.Data.Abstractions.Sources;

namespace Koan.Data.Core.Routing;

public sealed class DataSwitchInProgressException : InvalidOperationException
{
    public const string FailureCode = "koan.data.switch-in-progress";

    internal DataSwitchInProgressException(string source, DataOperationEffect effect)
        : base(
            $"Data route '{source}' is temporarily closed for {(effect == DataOperationEffect.Read ? "reads" : "writes")} " +
            $"({FailureCode}). Retry after the active default-route operation completes.")
    {
        RouteSource = source;
        Effect = effect;
    }

    public string Code => FailureCode;
    public string RouteSource { get; }
    public DataOperationEffect Effect { get; }
    public string Correction => "Retry after the active default-route operation completes.";
}
