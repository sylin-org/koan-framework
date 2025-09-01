# Sora.Flow.Core

Entity-first pipeline runtime and contracts. Auto-registered via ISoraAutoRegistrar.

Usage
- Add reference to Sora.Flow.Core; AddSora() will discover and register defaults.
- Configure via Sora:Flow options.

Model-typed surface (greenfield)
- FlowEntity<TModel>: canonical type marker
- DynamicFlowEntity<TModel> (Model property), StageRecord<TModel>: normalized deltas and hot-stage storage
- KeyIndex<TModel>, ReferenceItem<TModel>, ProjectionTask<TModel>
- ProjectionView<TModel,TView>, CanonicalProjection<TModel>, LineageProjection<TModel>

Helpers
- Attributes: [AggregationTag(path)], [FlowModel(name)], [FlowIgnore]
- FlowSets: per-model set names (flow.{model}.stage, flow.{model}.views.{view})
