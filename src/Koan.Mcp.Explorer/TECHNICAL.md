# Sylin.Koan.Mcp.Explorer — technical contract

## Composition ownership

`McpExplorerModule` is the only activation owner. It binds `Koan:Mcp:Explorer`, contributes Explorer sub-routes to
Koan Web, and supplies `IMcpConsoleRenderer` to MCP Core. Core retains ownership of the bare MCP GET and negotiates
between a browser console and Streamable HTTP; Explorer never maps a competing base route.

The package depends only on `Sylin.Koan.Mcp` plus the ASP.NET Core shared framework. It adds no Entity, persistence,
transport, session, or authentication implementation.

## Projection and execution

`McpSurfaceProjector` produces `/map.json` from the same caller-aware Entity/custom-tool projection used by protocol
listing. `AccessMapProjector` produces the privileged inverse view. Try-it resolves `EndpointToolExecutor` from the
request scope and passes `HttpContext.User`; it does not mint a token, proxy to MCP HTTP, or bypass the Entity gate.

`ExplorerAssetProvider` serves only embedded `index.html`, CSS, and JavaScript resources beneath its fixed manifest
prefix. It normalizes separators, rejects `..`, and maps a bounded content-type set.

## Failure and security boundaries

Malformed try-it bodies return 400, anonymous execution returns 401, and governed denial stays an explicit execution
failure/short-circuit. The privileged map returns 404 when its Development/admin decision fails. Explorer adds no
CSRF, rate-limit, proxy-header, or network-origin policy; applications exposing it remotely must compose those at the
ASP.NET Core edge.
