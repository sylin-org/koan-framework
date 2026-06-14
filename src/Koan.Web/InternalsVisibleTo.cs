using System.Runtime.CompilerServices;

// The official GraphQL connector hand-rolls its entity resolvers (it does not route through the
// internal EntityEndpointService), so it needs the canonical WEB-0068 predicate composer to apply
// the same hook-contributed visibility predicates the REST/MCP paths do. Exposed rather than made
// public to keep the composition helper an internal framework contract.
[assembly: InternalsVisibleTo("Koan.Web.Connector.GraphQl")]
