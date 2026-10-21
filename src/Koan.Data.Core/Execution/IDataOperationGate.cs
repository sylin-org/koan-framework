using Koan.Data.Abstractions.Sources;

namespace Koan.Data.Core.Execution;

/// <summary>Internal first-boundary source-policy gate used before deferring an Entity operation.</summary>
internal interface IDataOperationGate
{
    void Demand(DataOperationEffect effect, string operation);

    /// <summary>
    /// Everything the first operation of <paramref name="effect"/> would refuse before it reached the store, asked
    /// without doing it (NK-250): source policy, the bound segmentation, the registered storage guards and, for a
    /// write under an active scope, the adapter's isolation capability.
    /// </summary>
    void Preflight(DataOperationEffect effect, string operation);
}
