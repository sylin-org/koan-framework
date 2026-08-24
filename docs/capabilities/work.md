---
type: REFERENCE
domain: jobs
title: "Work and integration"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-23
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-23
  status: passed
  scope: docs/capabilities/work.md - route table verified against leaf targets
---

# Work and integration

Choose the business promise before choosing a queue. Owned execution, a named occurrence, and a
copy of current Entity state are three different capabilities wearing similar clothes - and the
wrong one costs a rewrite.

## Route by need

| The request says | Fetch |
|---|---|
| "do this in the background" / "run it nightly" / "return fast, finish later" | [background jobs](work/background-jobs.md) |
| "when X happens, tell Y" / "raise an event" | [events and transport](work/events-and-transport.md) |
| "another service needs the current state of this Entity" | [events and transport](work/events-and-transport.md) - Transport, not Events |

## Standing constraints

- Jobs own execution state and retry policy. Events own meaning. Transport owns delivery of
  current state. The promise decides the connector - never the reverse.
- Awaiting `Raise` or `Send` means the route accepted the items at its stated assurance boundary;
  it does not mean handler code finished.

## Do not, at this level

- Do not pick a queue or broker before naming the promise.
- Do not hand-roll retry loops, pollers, or background threads beside Jobs.

For the one-screen maturity view, see
[Work and integration in the capability map](../reference/capability-map.md#work-and-integration).
