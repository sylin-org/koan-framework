# Zen Garden Canonical Documentation Manifest

**Last Updated:** January 20, 2026  
**Total Files:** 57  
**Purpose:** Authoritative list of all canonical documentation files in the Zen Garden repository.

---

## Root Level (1 file)

| File | Purpose | Audience |
|------|---------|----------|
| `README.md` | Project front door - 30-second pitch, 2-minute mental model, quickstart | All |

---

## Navigation & Core (4 files)

| File | Purpose | Audience |
|------|---------|----------|
| `docs/README.md` | Documentation hub - audience-based navigation | All |
| `docs/START_HERE.md` | Complete beginner path - zero to first Stone in 30 minutes | Visitor, Operator |
| `docs/glossary.md` | Terminology reference - single source of truth for all terms | All |
| `docs/QA_CHECKLIST.md` | Documentation quality gates and validation checklist | Contributor |
| `docs/COORDINATION_REPORT.md` | Phase 1 discovery report for multi-agent cleanup | Contributor |

---

## Concepts (2 files)

| File | Purpose | Audience |
|------|---------|----------|
| `docs/concepts/overview.md` | Core concepts - Stones, discovery, configuration brittleness problem | All |
| `docs/concepts/architecture.md` | System architecture - how components fit together | Developer, Contributor |

---

## Guides (4 files)

| File | Purpose | Audience |
|------|---------|----------|
| `docs/guides/first-stone.md` | Installation guide - hardware to first service deployment | Operator |
| `docs/guides/hardware.md` | Hardware selection guide - tiers, recommendations, e-waste reframing | Operator |
| `docs/guides/offering-services.md` | Service lifecycle management - plant, upgrade, rest, wake, take away | Operator |
| `docs/guides/troubleshooting.md` | Common problems and solutions | Operator, Developer |

---

## Reference (8 files)

| File | Purpose | Audience |
|------|---------|----------|
| `docs/reference/api.md` | HTTP API endpoints - Moss daemon API reference | Developer |
| `docs/reference/offerings.md` | Service offerings catalog - available services | Operator, Developer |
| `docs/reference/ports.md` | Port allocation - reserved ports 7184-7199 | Operator, Developer |
| `docs/reference/connection-strings.md` | Connection string protocol - zen-garden:service/db format | Developer |
| `docs/reference/config.md` | Configuration reference - Moss daemon config options | Operator, Developer |
| `docs/reference/system-architecture.md` | Technical system architecture - project structure, components, abstractions | Developer, Contributor |
| `docs/reference/ai-navigation.md` | AI-friendly navigation layer - canonical sources for AI agents | AI, Contributor |
| `docs/reference/patterns/` | Technical design patterns | Developer, Contributor |
| `docs/reference/patterns/network-singleton-pattern.md` | Network service singleton pattern - socket reuse, graceful shutdown | Developer |

---

## Specifications (7 files)

| File | Purpose | Audience |
|------|---------|----------|
| `docs/specs/api-v1.md` | API v1 specification - dual-layer design (Offerings API + Services API) | Developer |
| `docs/specs/discovery.md` | Discovery protocol specification - mDNS, TXT records | Developer |
| `docs/specs/moss-daemon.md` | Moss daemon specification - HTTP API, Docker Compose, health monitoring | Developer |
| `docs/specs/offerings.md` | Service offerings specification - templates, taxonomy, query recommendations | Developer |
| `docs/specs/rake-cli.md` | Rake CLI specification - hot cache discovery, UDP broadcast, commands | Developer |
| `docs/specs/security.md` | Security specification - Pond architecture, mTLS, threat models (1418 lines) | Security, Developer |
| `docs/specs/technical.md` | Legacy comprehensive specification - complete development reference (2576 lines) | Developer, Contributor |

---

## Security (3 files)

| File | Purpose | Audience |
|------|---------|----------|
| `docs/security/overview.md` | Security overview - default plaintext, optional Pond mTLS | All |
| `docs/security/pond-setup.md` | Pond security setup - certificate management, admission | Security, Operator |
| `docs/security/threat-analysis.md` | Threat analysis - attack vectors, mitigations | Security |

---

## Operations (5 files)

| File | Purpose | Audience |
|------|---------|----------|
| `docs/ops/roadmap.md` | Development timeline and milestones | All |
| `docs/ops/release-notes.md` | Release history - current release, breaking changes, known issues | Operator |
| `docs/ops/maintainers.md` | Maintainer information | Contributor |
| `docs/ops/build-distribution.md` | Build process - artifacts, commands, release packaging | Contributor, Operator |
| `docs/ops/build-optimization.md` | Build optimization guide - release vs debug, size optimization | Contributor |

---

## Decisions (15 files)

Architecture Decision Records documenting design choices.

| File | Purpose | Category |
|------|---------|----------|
| `docs/decisions/README.md` | Decision index | Meta |
| `docs/decisions/API-0001-dual-layer-api.md` | Dual-layer API design (Offerings + Services) | API |
| `docs/decisions/BUILD-0001-versioning.md` | Build versioning strategy | Build |
| `docs/decisions/COMPAT-0001-compatibility.md` | Compatibility policy | Architecture |
| `docs/decisions/LANTERN-0001-registry.md` | Lantern registry service | Discovery |
| `docs/decisions/LANTERN-0003-mdns-service-discovery.md` | mDNS service discovery | Discovery |
| `docs/decisions/MDNS-0001-single-service-type.md` | Single mDNS service type | Discovery |
| `docs/decisions/MOSS-0001-registry.md` | Moss registry design | Architecture |
| `docs/decisions/OFFER-0001-taxonomy.md` | Offering taxonomy | Services |
| `docs/decisions/RAKE-0010-caching.md` | Rake discovery caching | CLI |
| `docs/decisions/SECURITY-0001-pond-tiers.md` | Pond security tiers | Security |
| `docs/decisions/SECURITY-0002-keystone-rename.md` | Keystone terminology | Security |
| `docs/decisions/SECURITY-0003-keystone-protection-tiers.md` | Keystone protection tiers | Security |
| `docs/decisions/STATE-0001-stateless-moss.md` | Stateless Moss design | Architecture |

---

## Proposals (11 files)

Active proposals for future features and evaluations.

| File | Purpose | Status |
|------|---------|--------|
| `docs/proposals/bridges.md` | Bridge specification - garden-to-garden federation with capability sharing | Active |
| `docs/proposals/ceremonies.md` | Ceremony specification - long-running distributed operations with Elder Stone coordination | Active |
| `docs/proposals/cli-taxonomy.md` | CLI command taxonomy | Active |
| `docs/proposals/GARDEN-NAMING-ASSESSMENT-REVIEW.md` | Naming assessment review | Active |
| `docs/proposals/naming-assessment.md` | Naming assessment | Active |
| `docs/proposals/offering-modes.md` | Offering modes - Planted (Docker), Adopted (native), Borrowed (external) service lifecycle | Active |
| `docs/proposals/passphrase-generation-ux.md` | Passphrase generation UX | Active |
| `docs/proposals/pebble-android.md` | Pebble Android app proposal | Active |
| `docs/proposals/stone-lifecycle.md` | Stone lifecycle management | Active |
| `docs/proposals/stone-profiles.md` | Stone profiles - Hearth, Workbench, Gateway, Full hardware role configurations | Active |
| `docs/proposals/totp-admission.md` | TOTP admission proposal | Active |

---

## Architecture (1 file)

| File | Purpose | Audience |
|------|---------|----------|
| `docs/architecture/joy-in-infrastructure.md` | Joy in infrastructure - design philosophy, golden standards | Contributor, Developer |

---

## Tests (1 file)

| File | Purpose | Audience |
|------|---------|----------|
| `tests/README.md` | Test documentation - running tests, test structure | Contributor |

---

## Source (1 file)

| File | Purpose | Audience |
|------|---------|----------|
| `src/rake/CHANGELOG.md` | Rake CLI changelog | All |

---

## Manifests (2 files)

| File | Purpose | Audience |
|------|---------|----------|
| `manifests/README.md` | Service manifests documentation | Developer |
| `manifests/COMPATIBILITY_SOURCES.md` | Compatibility sources for service versions | Developer |

---

## Installer (4 files)

| File | Purpose | Audience |
|------|---------|----------|
| `installer/stone-root/README.md` | Stone root filesystem documentation | Developer |
| `installer/branding/README.md` | Branding assets documentation | Contributor |
| `installer/branding/source/ASSET-SPECS.md` | Asset specifications | Contributor |
| `installer/branding/source/COLOR-PALETTE.md` | Color palette reference | Contributor |

---

## Change Log

| Date | Change | Files Affected |
|------|--------|----------------|
| 2026-01-20 | Phase 1 coordination complete | +1 COORDINATION_REPORT.md, +1 CANON_MANIFEST.md |
| 2026-01-20 | Phase 3 cleanup execution | -13 stray files, moved 6 files |
| 2026-01-20 | Moved ARCHITECTURE.md | → docs/reference/system-architecture.md |
| 2026-01-20 | Moved BUILD-DISTRIBUTION.md | → docs/ops/build-distribution.md |
| 2026-01-20 | Moved BUILD_OPTIMIZATION.md | → docs/ops/build-optimization.md |
| 2026-01-20 | Moved network-singleton-pattern.md | → docs/reference/patterns/network-singleton-pattern.md |
| 2026-01-20 | Flattened ai/index.md | → docs/reference/ai-navigation.md |
| 2026-01-20 | Deleted stray implementation artifacts | -7 root files, -3 proposal files, -2 test reports, -1 STRAY_REPORT.md |
| 2026-01-18 | Greenfield transformation complete | 48 canonical files established |

---

## Validation

**Last Validated:** January 20, 2026

- ✅ All files listed exist in repository
- ✅ All files linked from docs/README.md
- ✅ No orphan documentation files
- ✅ No stub or redirect files
- ✅ No root-level markdown except README.md
- ✅ Consistent frontmatter across canonical files
- ✅ All internal links validated

---

## Maintenance

**How to update this manifest:**

1. When adding new canonical documentation:
   - Add entry to appropriate section above
   - Update total file count
   - Add change log entry
   - Update docs/README.md navigation

2. When moving/renaming files:
   - Update file path in manifest
   - Add change log entry
   - Update all internal links

3. When archiving/deleting files:
   - Remove from manifest
   - Update total file count
   - Add change log entry
   - Ensure no broken links remain

**Validation command:**
```powershell
# Verify all manifest files exist
Get-Content docs/CANON_MANIFEST.md | 
  Select-String -Pattern '`([^`]+\.md)`' | 
  ForEach-Object { 
    $file = $_.Matches[0].Groups[1].Value
    if (-not (Test-Path $file)) { 
      Write-Output "MISSING: $file" 
    }
  }
```
