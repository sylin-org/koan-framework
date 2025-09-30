# DATA-0073: Use JObject for Dynamic Entity Models

- Status: accepted
- Date: 2025-09-10
- Deciders: Copilot

## Context and Problem Statement

When processing dynamic entities within `Koan.Canon.Core`, the system encountered critical serialization errors at the data persistence layer. Specifically, `MongoDB.Bson.BsonSerializationException` and `System.FormatException` were thrown when attempting to serialize or deserialize models based on `System.Dynamic.ExpandoObject`.

The root cause was traced to the inherent limitations of the default MongoDB C# driver serializers when handling the dynamic nature of `ExpandoObject`, especially when the object contained non-standard or primitive BSON types that could not be cleanly mapped to a dictionary representation. This led to unpredictable runtime failures, particularly when data was passed through messaging systems and its shape was not strictly controlled.

## Decision Drivers

- **Serialization Robustness**: The primary driver was the need for a predictable and robust serialization mechanism that could handle any valid JSON structure without failure.
- **Type Fidelity**: `ExpandoObject`'s representation as `IDictionary<string, object>` was not a perfect match for complex JSON documents, leading to type mismatches and serialization failures.
- **Developer Experience**: The errors were difficult to debug, as they occurred deep within the serialization layer and were dependent on the specific data being processed.

## Considered Options

1.  **Continue using `ExpandoObject` with a more complex custom serializer**: This was deemed too brittle. The complexity of correctly handling all edge cases for `ExpandoObject` would be high and likely lead to future maintenance burdens.
2.  **Switch to `Newtonsoft.Json.Linq.JObject`**: `JObject` is a purpose-built model for representing JSON documents in memory. It provides a much richer and more explicit structure than `ExpandoObject`. The `Newtonsoft.Json` library is already a dependency in the project, so this introduces no new external dependencies.
3.  **Use `System.Text.Json.JsonDocument`**: While a viable alternative, `JObject` was chosen due to its established use in the project and the team's familiarity with the `Newtonsoft.Json` API.

## Decision Outcome

Chosen option: "Switch to `Newtonsoft.Json.Linq.JObject`".

This decision was implemented by:

1.  **Creating a Custom `JObjectSerializer`**: A new serializer (`Koan.Data.Connector.Mongo.Initialization.JObjectSerializer`) was created to handle the serialization and deserialization of `JObject` to and from BSON. This serializer is designed to be robust, correctly handling both BSON documents and primitive types by wrapping primitives in a standard `{"value": ...}` structure.
2.  **Creating a `JObjectSerializationProvider`**: A corresponding `IBsonSerializationProvider` was created to register the custom serializer with the MongoDB driver for types `JObject` and `object`.
3.  **Refactoring Core Components**: All instances of `ExpandoObject` in `Koan.Canon.Core` were replaced with `JObject`. This included `IDynamicFlowEntity`, `DynamicFlowEntity<TModel>`, and various extension methods.
4.  **Updating Orchestration Logic**: The `FlowOrchestratorBase` was updated to materialize incoming message payloads directly into `JObject`, ensuring that the model is treated consistently throughout the processing pipeline.

### Positive Consequences

- **Eliminated Serialization Errors**: The change completely resolved the `BsonSerializationException` and `FormatException` errors.
- **Improved Type Safety**: `JObject` provides a more accurate in-memory representation of JSON data, reducing the likelihood of type-related errors.
- **Simplified Logic**: The custom serializer encapsulates the complexity of handling different BSON types, simplifying the application logic that consumes the data.

### Negative Consequences

- **Refactoring Effort**: This was a significant refactoring that touched multiple projects and required careful testing to ensure correctness.
- **Dependency on `Newtonsoft.Json`**: This change further solidifies the project's dependency on `Newtonsoft.Json` for dynamic object modeling.

## ADR-0053: `Koan-flow` pillar, entity-first and auto-registrar

This decision aligns with the principles of `ARCH-0053`, which emphasizes an entity-first approach. By choosing a robust representation for our dynamic entities (`JObject`), we ensure that the entity model is reliable and can be handled consistently by the underlying infrastructure, including the auto-registrar and persistence layers.

