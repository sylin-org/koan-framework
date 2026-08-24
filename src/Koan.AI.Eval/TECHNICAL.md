# Sylin.Koan.AI.Eval technical contract

## Activation and ownership

Generated module activation registers one singleton `IEvalService`. Static `Eval.*` methods resolve it from the active
host. `ModelRef`, `DatasetRef`, scores, and job vocabulary come from the inert shared AI contracts boundary.

## Capability and behavior

Every adapter-backed measurement resolves `AiCapability.MetricCompute`; no compatible adapter is a corrective
failure. The capability is structural: an adapter that declares it must also implement
`IMetricAdapter` (in `Sylin.Koan.AI.Contracts`), and one that declares the flag without the interface fails
correctively at measurement time naming both facts. `Measure` computes each requested metric through that
adapter. `Gate` evaluates min/max and baseline-regression conditions and throws typed violations; a standalone
`NoRegression` condition (no `Metric(...)` named) or a regression condition without a baseline is refused with a
correction instead of passing vacuously. `Compare` measures each model and orders by average score. `Regress`
converts gate violations into `Passed=false`. `Drift` is an in-process comparison of shared metric values.

No connector shipped in-tree implements `IMetricAdapter` today: until you reference or write one, every
measurement call fails with the correction above. That is deliberate - metric computation without a real
implementation must not answer zero.

## Limits

The adapter owns dataset lookup and metric computation. The package owns no dataset repository, sampling policy,
confidence interval, deployment controller, timer, alert channel, or durable evaluation ledger. Cancellation flows to
adapter work; provider exceptions remain visible.
