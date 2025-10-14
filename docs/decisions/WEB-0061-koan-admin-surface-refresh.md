# WEB-0061 Koan Admin surface refresh

**Status**: Accepted 2025-10-13 \
**Drivers**: Koan Admin UX, LaunchKit DX, manifest visibility \
**Deciders**: Koan Framework maintainers \
**Inputs**: Koan Admin status payloads, KoanAdminManifestService, LaunchKit metadata \
**Outputs**: Updated admin SPA, enriched status API, documentation \
**Related**: WEB-0044, WEB-0051

## Context

The current Koan Admin dashboard surfaces only coarse-grained module summaries, a flat health grid, and a LaunchKit trigger. Teams have asked for richer diagnostics while staying within the zero-config posture. Specifically we need:

- Module-level detail including notes and current settings (with secret masking) so engineers know what bootstrapped and why.
- Expanded health reporting that surfaces fact payloads, timestamps, and messages, not just status badges.
- A visible capability snapshot that mirrors `KoanAdminFeatureSnapshot`, allowing operators to confirm which surfaces are actually enabled (web UI, manifest, destructive operations, LaunchKit, transcript downloads).
- Better LaunchKit affordances, including describing bundle contents and listing OpenAPI templates.
- A manual refresh and optional auto-refresh so the page stays accurate during live changes.

The backend already collects boot reports and health snapshots, but the data is not exposed in the `/api/status` response. The SPA renders minimal information and omits the new capability toggles that were added for auth parity work.

## Decision

1. Introduce a sanitized module surface (`KoanAdminModuleSurface`) returned with the status payload. Mask secret values and include module notes so the UI can render drill-down details without exposing sensitive data.
2. Extend the status response to include a flattened list of startup notes and the full `KoanAdminHealthDocument` components so the SPA can expose timestamps, messages, and fact metadata per health contributor.
3. Refresh the Koan Admin SPA:
   - Add a capabilities panel showing each `KoanAdminFeatureSnapshot` flag and the current admin route map.
   - Render modules as an accordion with notes and masked settings, sorted alphabetically with quick filters.
   - Show health components with expanders summarizing status, message, timestamp, and facts, and provide manual reload.
   - Improve LaunchKit UX by listing available assets, explaining each bundle toggle, and showing when the feature is disabled for the host.
   - Surface the last refresh timestamp and wire an optional auto-refresh cadence (default off, 30-second interval when enabled).
4. Document the change in this ADR and update Koan Admin README/technical references where applicable.

## Rationale

- Returning sanitized module details keeps secrets safe while avoiding another hop to fetch a manifest. This satisfies the request for module notes without exposing raw configuration.
- Health insights depend on a consistent payload that shows facts and timestamps; reusing the existing health contract avoids inventing a parallel structure.
- Operators need to understand which admin capabilities are enabled, especially with development-only defaults (e.g., auto policy). Mirroring the feature snapshot reduces confusion during triage.
- LaunchKit is a flagship DX feature; polishing the interaction and messaging supports packaging workflows.
- Providing refresh controls makes the surface useful during iterative configuration changes without requiring a page reload.

## Consequences

- `/api/status` payloads grow slightly; front ends that rely on them must tolerate the additional properties. No breaking changes are expected because we only add members.
- The SPA gains more logic and styles; testing should cover the new rendering paths and refresh loop. We will add targeted UI checks in the validation script.
- Manifest exposure rules stay in place. Operators still need to opt-in to the full manifest endpoint; the new sanitized payload ensures the dashboard remains functional even when the manifest is locked down.
- Future capability toggles should be added to the feature snapshot in one place to keep the UI accurate. The ADR should be revisited if we introduce server-driven layouts or multi-tenant scopes.

## Implementation notes

- Update `KoanAdminStatusController` to map `KoanAdminManifest` into sanitized module surfaces and aggregate startup notes.
- Add the new contracts under `src/Koan.Web.Admin/Contracts/`.
- Rework `wwwroot/index.html`, `app.js`, and `styles.css` to implement the refreshed layout and behaviour.
- Provide follow-up documentation updates (Koan Admin README) and rerun `dotnet build` plus sample smoke as needed.
