---
id: SEC-0035
slug: signing-out-takes-a-post
domain: Security
status: Accepted
date: 2026-10-30
title: Signing out takes a POST
related:
  - SEC-0029
---

# SEC-0035: Signing out takes a POST

## Context

`/auth/logout` answered `GET` as well as `POST` and ended the session on either. An application that shows anything a
person wrote can show an image, and an image is a `GET` carrying the reader's cookie: `<img src="/auth/logout">` in a post
signed out whoever read it, including whoever could have removed it.

## Decision

**`/auth/logout` answers `POST` only.** A `GET` is 405 and ends nothing. A page signs out with a form, which needs no
script. `return` keeps the rule of SEC-0029.

## Consequences

- Behavioural: a link to `/auth/logout` no longer signs out; the application makes it a form. No migration.
- Evidence: `ReturnLandingSpec` holds a `GET` at 405 and every return on a `POST`.
