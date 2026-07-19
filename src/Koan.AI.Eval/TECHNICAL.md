# Sylin.Koan.AI.Eval technical contract

## Activation and ownership

Generated module activation registers one singleton `IEvalService`. Static `Eval.*` methods resolve it from the active
host. `ModelRef`, `DatasetRef`, scores, and job vocabulary come from the inert shared AI contracts boundary.

## Capability and behavior

Every adapter-backed measurement resolves `AiCapability.MetricCompute`; no compatible adapter is a corrective
failure. `Measure` computes each requested metric. `Gate` evaluates min/max and optional baseline-regression
conditions and throws typed violations. `Compare` measures each model and orders by average score. `Regress` converts
gate violations into `Passed=false`. `Drift` is an in-process comparison of shared metric values.

## Limits

The adapter owns dataset lookup and metric computation. The package owns no dataset repository, sampling policy,
confidence interval, deployment controller, timer, alert channel, or durable evaluation ledger. Cancellation flows to
adapter work; provider exceptions remain visible.
