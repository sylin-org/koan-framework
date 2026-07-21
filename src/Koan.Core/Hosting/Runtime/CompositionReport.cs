using Koan.Core.Diagnostics;

namespace Koan.Core.Hosting.Runtime;

internal sealed record CompositionReport(string? Line, IReadOnlyList<KoanFact> Facts);
