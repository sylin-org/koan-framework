# Sylin.Koan.AI.Web — technical contract

## Composition ownership

`AiWebModule` is the single activation owner. During `AddKoan()` it registers the assembly containing `AiController`
with MVC through Koan Web's standard application-part seam. It registers no provider registry, AI pipeline, health
subscriber, middleware, or duplicate routing policy.

The package depends explicitly on `Sylin.Koan.AI`, `Sylin.Koan.AI.Contracts`, `Sylin.Koan.Web`, and
`Sylin.Koan.Web.Sse`. A package reference therefore declares projection availability while provider references declare
which native operations can execute.

## Execution

Controller actions use `IAiPipeline` for chat, streaming, and embeddings; semantic `Client` behavior for OCR; and the
read-only `IAiAdapterRegistry` for inspection and explicit model-management requests. SSE emits non-empty text deltas
and honors request cancellation. Model inventory captures a bounded adapter id/message for each provider that failed
while listing models so partial results are inspectable.

## Failure and security boundaries

ASP.NET Core model binding owns malformed-request responses. Missing providers or capabilities fail through the AI
runtime. Native HTTP/model failures are not normalized into promised 408/429/502 categories. The package adds no
authorization metadata, body-size limit, CORS policy, retry, buffering threshold, or rate limiter. Applications must
compose those concerns explicitly when exposing the routes beyond a trusted boundary.
