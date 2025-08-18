Sora.Messaging.Inbox.Http

Purpose
- Provides an `IInboxStore` implementation that talks to an external Inbox microservice over HTTP using the standardized v1 API.

Behavior
- When the package is referenced and `Sora:Messaging:Inbox:Endpoint` is set, Sora auto-registers the HTTP inbox store and replaces any previously registered store.
- Timeouts are conservative (default 5s). Errors in status lookups are treated as "not processed" to avoid false positives.

Configuration
- Sora:Messaging:Inbox:Endpoint: Base URL of the inbox service, e.g. `http://inbox:8080`.

Notes
- This client expects the endpoints defined in ADR-0025 (`GET /v1/inbox/{key}` and `POST /v1/inbox/mark-processed`).
