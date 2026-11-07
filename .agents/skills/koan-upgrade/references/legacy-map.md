# Older-code fingerprints

Use these only to find migration seams. Confirm the application's source version and prove the current replacement before changing anything.

| Fingerprint | Investigate | Do not assume |
|---|---|---|
| Dependency ID beginning `Koan.` | Current dependency identity | Namespace and dependency names changed together |
| `DataQueryOptions` | Current query contract and member semantics | Members map by name |
| `AddKoanMcp()` or manual MCP endpoint mapping | Current MCP composition and any custom extension | Every manual call is obsolete |
| `AddKoanWeb()` | Current Web composition and real option/policy setup | The whole call can be deleted |
| `QueryCaps` | Current capability API | A mechanical rename exists |
| `IPayloadTransformer` | Current public transformation extension | An internal type is a supported replacement |
| `McpToolSchema` or `IMcpTool` | Current tool/resource contract | Generated and custom tools migrate identically |
| `[VectorField]` | Current embedding/vector expression | Provider or stored vectors may change implicitly |
| An exception filter or middleware mapping `EntityLifecycleCancelledException` or `Refusal` to an answer | WEB-0086: an entity write answers both as a Problem whose `word` is the cancellation's code or the refusal's word, so the filter no longer sees them there and a client reading `code` reads `word` | The filter can go: the application's own actions still throw to it |

Do not flag a token merely because it is old in another application. Current Koan may still use `SaveAsync`, `Count.Exact()`, `Count.Fast()`, `AllStream()`, `QueryStream()`, `EntityLifecycleBuilder`, lifecycle context `Entity`, `CapabilitySet.Has(DataCaps...)`, and `Koan.*` namespaces.

Never rename a configuration key from memory. Verify current binding, aliases, precedence, secret handling, and failure behavior. Preserve environment-specific values and never print secrets.
