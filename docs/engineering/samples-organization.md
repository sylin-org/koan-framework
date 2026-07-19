---
type: DEV
domain: framework
title: "Sample portfolio standard"
audience: [maintainers, contributors]
status: current
last_updated: 2026-07-19
framework_version: v0.20.0
---

# Sample portfolio standard

Koan samples are product evidence and curriculum, not a numbered inventory of repository experiments.
Their structure should answer what a reader is trying to learn before it exposes implementation detail.

## Portfolio shape

```text
samples/
  FirstUse/                 # shortest complete product result
  GoldenJourney/            # growth of the FirstUse application
  fundamentals/             # one concern in isolation
    LocalChecklist/
    TaskGraph/
  journeys/                 # one application growing cumulatively
    GardenCoop/
      01-GardenJournal/
      02-LocalDiscovery/
  applications/             # complete, business-shaped applications
    DevPortal/
```

Numbers are allowed only when order carries product meaning. A journey chapter must be a strict superset
of the preceding chapter: preserve its complete business result and add one visible capability. Unrelated
applications use semantic names, not global sequence numbers.

## Placement rules

- `fundamentals/` contains the smallest honest proof of one framework concern.
- `journeys/` demonstrates V0-to-V1 growth in meaningful small steps.
- `applications/` holds coherent business applications, including work still being graduated.
- A multi-project application may group `Api`, `Worker`, or similarly earned process roles beneath one
  application root. Do not create empty shells or speculative subprojects.
- Shared code is promoted only after real reuse; samples should not hide their story behind a generic sample kit.

## Graduation rules

A sample joins `samples/README.md` only when all of these agree:

1. one business sentence and one shortest meaningful command;
2. business-first application code using the canonical `AddKoan()` and Entity grammar;
3. an executable cumulative contract proving result, host surface, and composition facts;
4. honest prerequisites, provider behavior, errors, and deployment claims;
5. current paths and names across source, solution, requests, dashboard, and documentation.

Project presence is not a support claim. Work that has not graduated remains outside the public curriculum;
dead, duplicate, or speculative samples are deleted instead of archived in the active tree.

## Application code standard

Prefer intent-to-code expressions: `Entity<T>`, `EntityController<T>`, Entity capability rings, ordinary
attributes, and standard .NET configuration. References state capability intent. Application modules are
earned only when the application owns real composition, startup data, or reporting policy. Provider selection,
middleware order, discovery, and repeated mechanics belong at framework chokepoints.

Use Entity statics for bounded materialized work. Demonstrate `AllStream` or `QueryStream` only where the
selected adapter supplies provider-bounded streaming; otherwise use explicit paging.

## Change checklist

- Decide whether the artifact is a fundamental, cumulative journey chapter, or complete application.
- Name it for the business result; number only a journey chapter.
- Move source, tests, solution entries, lockfiles, requests, and docs as one logical change.
- Run the focused sample contract and any capability-owner tests exposed by dogfood.
- Update `samples/README.md` only after graduation; state unfinished application work explicitly.
- Leave ADRs unchanged. Historical names in decisions remain historical evidence.
