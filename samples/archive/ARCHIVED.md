# Archived Samples

This directory contains samples that have been archived as part of the strategic sample collection realignment (see `docs/decisions/DX-0045-sample-collection-strategic-realignment.md`).

## Why These Were Archived

These samples were archived because they:
- Lacked comprehensive documentation (no README)
- Had unclear value propositions or overlapping functionality
- Were made redundant by newer, more comprehensive samples
- Created confusion in the sample numbering scheme

## Migration Guidance

| Archived Sample | Reason | Recommended Alternative |
|----------------|--------|------------------------|
| **S2** | No README, unclear purpose | S1.Web for basic CRUD, S10.DevPortal for framework showcase |
| **S4.Web** | No README, no clear value | S1.Web for web basics |
| **S6.Auth** | Redundant with other auth demos | S5.Recs or S7.DocPlatform for authentication patterns |
| **S6.SocialCreator** | No README, unclear status | S5.Recs or S16.PantryPal for complex app patterns |
| **S12.MedTrials** | Sparse docs, unclear value vs S16 | S16.PantryPal for MCP Code Mode demonstration |
| **S15.RedisInbox** | Too minimal | S3.NotifyHub for comprehensive inbox pattern demonstration |
| **KoanAspireIntegration** | Integration example, not sample app | Integration examples should be in separate location |

## Sample History

These samples are preserved in the git history and in this archive for reference. They are not maintained and may not work with current framework versions.

## Need Something From an Archived Sample?

If you need functionality from an archived sample:

1. **Check the recommended alternatives** in the table above
2. **Review the sample catalog** at `samples/README.md` for current samples
3. **Search by capability** using the capability matrix in the catalog
4. **Ask the community** if you can't find what you need

---

**Archival Date**: 2025-10-16
**Framework Version at Archive**: v0.6.3
**Decision Reference**: docs/decisions/DX-0045-sample-collection-strategic-realignment.md
