# .NET 10 RC 1 Migration Documentation

This directory contains documentation for migrating Koan Framework to .NET 10 RC 1.

## Quick Start

1. **Install .NET 10 RC 1**
   Follow the [Installation Guide](./DOTNET-10-RC1-INSTALLATION.md)

2. **Review Migration Plan**
   Read the [Migration Plan](./DOTNET-10-MIGRATION-PLAN.md) for detailed steps

3. **Execute Migration**
   Follow the 9-phase plan systematically

## Documentation

### [DOTNET-10-RC1-INSTALLATION.md](./DOTNET-10-RC1-INSTALLATION.md)
Complete installation guide for .NET 10 RC 1 SDK across Windows, Linux, macOS, and Docker environments.

**Contents**:
- Installation instructions for all platforms
- IDE configuration (VS 2026, VS Code, Rider)
- Global.json configuration options
- NuGet prerelease package setup
- CI/CD integration examples
- Troubleshooting common issues
- Rollback procedures

### [DOTNET-10-MIGRATION-PLAN.md](./DOTNET-10-MIGRATION-PLAN.md)
Comprehensive migration plan for Koan Framework.

**Contents**:
- 9-phase migration strategy
- Detailed task breakdown
- Breaking changes remediation
- Koan Framework pattern validation
- Testing strategies
- Container/Docker updates
- Documentation requirements
- Risk mitigation
- Timeline (2-3 weeks)

## Migration Status

**Branch**: `feature/dotnet-10-rc1-migration`

**Current Phase**: Phase 0 - Planning Complete ✅

**Next Steps**:
1. Install .NET 10 RC 1 SDK on development machines
2. Verify SDK installation: `dotnet --version`
3. Begin Phase 1: Foundation & Preparation

## Quick Commands

```bash
# Verify .NET 10 RC 1 installation
dotnet --version
# Expected: 10.0.100-rc.1.25451.107

# Check current branch
git branch
# Should show: * feature/dotnet-10-rc1-migration

# View migration plan
cat docs/migration/DOTNET-10-MIGRATION-PLAN.md
```

## Support

- **Issues**: https://github.com/sylin-labs/Koan-framework/issues
- **Discussions**: https://github.com/sylin-labs/Koan-framework/discussions
- **Internal**: See migration plan for contact information

## Timeline

| Phase | Status | Duration |
|-------|--------|----------|
| Phase 0: Planning | ✅ Complete | - |
| Phase 1: Foundation | 🔜 Pending | 2-3 days |
| Phase 2: Dependencies | 🔜 Pending | 3-5 days |
| Phase 3: Breaking Changes | 🔜 Pending | 3-4 days |
| Phase 4: Pattern Validation | 🔜 Pending | 4-6 days |
| Phase 5: Sample Testing | 🔜 Pending | 3-4 days |
| Phase 6: Container Testing | 🔜 Pending | 2-3 days |
| Phase 7: Test Suite | 🔜 Pending | 2-3 days |
| Phase 8: Documentation | 🔜 Pending | 1-2 days |
| Phase 9: Release | 🔜 Pending | 1 day |

**Total Estimated Duration**: 18-31 days (2-4 weeks)

## Key Decisions

### SDK Pinning
Using `global.json` to pin to specific RC version for build consistency:
```json
{
  "sdk": {
    "version": "10.0.100-rc.1.25451.107",
    "rollForward": "latestMinor",
    "allowPrerelease": true
  }
}
```

### Package Versioning Strategy
- All Microsoft.Extensions.* → `10.0.0-rc.1.25451.107`
- Database providers → Latest .NET 10 compatible versions
- Aspire → `10.0.0-rc.1.*` (when available)
- Third-party → Verify compatibility, update if needed

### Breaking Changes Priority
1. **High**: Cookie authentication (affects S6 samples)
2. **Medium**: WebHostBuilder obsolescence (search for usage)
3. **Low**: Swagger WithOpenApi (affects connector)

## Risk Assessment

| Risk Level | Components | Mitigation |
|------------|------------|------------|
| **High** | Koan Framework patterns (auto-registration, entities) | Comprehensive Phase 4 testing |
| **Medium** | Database provider compatibility | Early testing, fallback versions |
| **Medium** | Breaking changes in ASP.NET Core | Systematic remediation in Phase 3 |
| **Low** | Container builds | Test early, use specific tags |

## Success Criteria

- ✅ All 100+ projects build successfully
- ✅ 100% test pass rate
- ✅ All Koan Framework patterns validated
- ✅ All samples functional
- ✅ No critical performance regressions (< 10%)
- ✅ Complete documentation
- ✅ Docker containers working

## Notes

- This is a **Release Candidate** migration - expect potential issues
- .NET 10 RC 1 has **go-live license** from Microsoft
- Final .NET 10 GA is scheduled for **November 2025**
- Migration provides early preparation for LTS release

---

**Last Updated**: 2025-10-01
**Migration Branch**: `feature/dotnet-10-rc1-migration`
**Responsible**: Enterprise Architect
**Status**: 📋 Planning Complete - Ready to Execute
