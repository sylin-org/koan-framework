# Sylin.Koan.AI.Agents technical contract

## Activation and ownership

Generated module activation registers one singleton `IAgentExecutor`. `Agent.Create()` builds an immutable
`AgentDefinition`; `Run`/`Stream` resolve the executor from the active `AppHost`. The package depends on AI runtime,
AI Orchestration tools, Data, Vector, and inert AI contracts because those are executed capabilities, not copied
vocabulary.

## Execution

The executor owns the bounded ReAct loop, provider calls, tool dispatch, accumulated steps, token accounting, and
terminal `AgentStatus`. Entity bindings are translated into tools at execution; `AllowWrite` is false unless the
application opts in. Search bindings use the current Vector path. Builder instances are immutable and safe to reuse.

## Failure and lifecycle

The host owns executor/provider/Data lifetimes. Missing host or registration is corrective. Provider, parse, tool,
Entity, and cancellation failures are not downgraded. Max iterations/tokens can produce a non-completed result; they
do not roll back completed tool effects. Memory objects describe conversation behavior but do not create durable Jobs,
approval, distributed locks, or a security sandbox.
