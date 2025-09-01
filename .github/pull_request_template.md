Title: <concise summary>

Why
- What problem does this change solve? Link issues/ADRs if applicable.

What changed
- Short bullet list of key changes.

Checklist (required)
- [ ] Per-project docs updated per ARCH-0042 (README.md + TECHNICAL.md) for affected modules
- [ ] No inline endpoints; routes exposed via controllers only (WEB-0035)
- [ ] Data samples use first-class model statics (All/Query/FirstPage/Page/Stream), not generic facades (DATA-0061)
- [ ] No magic values: constants/options centralized (ARCH-0040)
- [ ] Build/tests pass locally; new public behavior has minimal tests

Docs touchpoints (if applicable)
- [ ] Updated or linked relevant guides/refs under docs/reference or docs/guides
- [ ] Added/updated ADR entry and registered it in docs/decisions/toc.yml
- [ ] Verified Modules index lists or links to new/changed module docs

Validation notes
- Build: dotnet build
- Tests: scripts/test-*.ps1 or test tasks

References
- Engineering: docs/engineering/index.md
- Architecture principles: docs/architecture/principles.md
- Decision: docs/decisions/ARCH-0042-per-project-companion-docs.md