# Production APIs

Add DTOs, validation, and better error handling to your TaskFlow API.

## Goals
- Separate API contracts (DTOs) from entities
- Add validation attributes
- Ensure consistent error responses

## DTOs

```csharp
public record CreateTaskDto(string Title, string? Description);
public record TaskDto(Guid Id, string Title, string? Description, bool IsCompleted);
```

## Controller Improvements

- Use DTOs for input/output
- Validate ModelState
- Return ProblemDetails for errors

## OpenAPI

Sora.Web.Swagger auto-registers Swagger services. In Development, the UI is enabled by default. Optionally call `UseSoraSwagger()` to force-enable the UI.

Next: Commands & Events → 04-commands-and-events.md
