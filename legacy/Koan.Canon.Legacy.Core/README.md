# Koan.Canon.Core

Entity-first pipeline runtime and contracts. Auto-registered via IKoanAutoRegistrar.

Usage
- Add reference to Koan.Canon.Core; AddKoan() will discover and register defaults.
- Configure via Koan:Canon options.

Model-typed surface (greenfield)
- CanonEntity<TModel>: canonical type marker
- DynamicCanonEntity<TModel> (Model property), StageRecord<TModel>: normalized deltas and hot-stage storage
- KeyIndex<TModel>, ReferenceItem<TModel>, ProjectionTask<TModel>
- ProjectionView<TModel,TView>, CanonicalProjection<TModel>, LineageProjection<TModel>

Helpers
- Attributes: [AggregationTag(path)], [FlowModel(name)], [FlowIgnore]
- CanonSets: per-model set names (flow.{model}.stage, flow.{model}.views.{view})



