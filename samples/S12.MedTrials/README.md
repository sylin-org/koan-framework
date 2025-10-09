# S12.MedTrials — Sample Family

Components

- API/ — MedTrials HTTP API (controllers, properties, Compose-ready Dockerfiles)
- Core/ — Move target for shared domain/contracts (previously S12.MedTrials.Core)
- MCP/ — Move target for MCP service (previously S12.MedTrials.McpService)

Run

- API: open `API/` and run `start.bat`.

Notes

- Entity-first patterns; controllers only; minimal Program via AddKoan.
- See `docs/engineering/samples-organization.md` for the family layout standard.