using Koan.Data.Abstractions.Sources;

namespace Koan.Data.Core.Execution;

/// <summary>Internal first-boundary source-policy gate used before deferring an Entity operation.</summary>
internal interface IDataOperationGate
{
    void Demand(DataOperationEffect effect, string operation);
}
