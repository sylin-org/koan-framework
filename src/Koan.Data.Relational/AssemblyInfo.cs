using System.Runtime.CompilerServices;

// ComparableScalarEncoding moved to Koan.Data.Core in 2026-08 (PMC-037), because Couchbase is governed by the
// same contract and a document store cannot reference this assembly. The type kept its full name so the move
// stays invisible to anything compiled against the published 1.0.0 package: a forwarder is what .NET provides
// for exactly this, and package validation understands it. Removing the name outright would break the 1.0
// compatibility train, which is enforced when the package is built rather than when the solution is.
[assembly: TypeForwardedTo(typeof(Koan.Data.Core.ComparableScalarEncoding))]
