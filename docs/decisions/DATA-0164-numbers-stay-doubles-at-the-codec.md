---
id: DATA-0164
slug: numbers-stay-doubles-at-the-codec
domain: Data
status: Accepted
date: 2026-10-30
title: Numbers stay doubles at the codec, and each adapter handles the rest at its edge
related:
  - DATA-0100
  - DATA-0139
  - DATA-0162
---

# DATA-0164: Numbers stay doubles at the codec, and each adapter handles the rest at its edge

## Context

Measured at `bd70b7b01`. `RelationalStructuredValueCodec` turned every JSON real number into a decimal: 1/3 read back as
0.333333333333333, and a save above about 7.9e28 threw. On SQL Server a DateTime bound as the legacy `datetime`, rounded to 1/300
of a second, ids differing only by case merged in an upsert, and two concurrent upserts of a new id threw.

## Decision

1. **The codec leaves a real number a double.** A `decimal` member reads its digits where its type is known, when the document
   deserializes into the Entity, so 19.99 and 0.1 stay exact and only code that needs a decimal pays. A whole number past a `long`
   reads as a double too, since PostgreSQL's `jsonb` prints a large double without an exponent.
2. **SQL Server ids are exact.** A text id column is `Latin1_General_BIN2`. Every other text column keeps the server's default
   collation and behaviour, and `FilterSupport.OrdinalStrings` stays false. MySQL's id column was already `utf8mb4_bin`.
3. **SQL Server's upsert has one winner and no error.** An update that finds no row inserts; a primary-key violation (2627) means
   the row exists after all, and the update runs again. A row it still cannot touch is another scope's, and only then is the write
   refused as cross-scope. No lock hint is taken.
4. **SQL Server binds a DateTime to the tick,** as `datetime2(7)`, inside the adapter (`SqlServerDateTime`). The DateTime set type
   is `datetime2(7)`, a new type name, created as any other.
5. **No migration.** A SQL Server table created earlier keeps its id collation until it is rebuilt.

## The limit

A number in a document is ordinary when a double holds it: a decimal of up to fifteen significant digits, a double of ordinary size.
Past that, what a store and its comparison type hold decides, and Koan does not build for it:

- a decimal past seventeen significant digits rounds, and one at the very top of its range saves and then refuses to read;
- on SQL Server a document double compares as `decimal(38, 10)`, so `Eq` on 1/3 misses and a value past 1e28 compares as NULL; one
  held in exponent form (below 1e-4, from 1e15) reaches it through `float`, whose conversion from text can land values of 1e16 and
  above a unit off;
- on MySQL the `JSON` type lands some doubles a unit off (7.9e28), a document double past 1e35 fails the save in its generated
  column, and `Eq` compares through `DECIMAL(65, 30)`;
- on SQL Server and MySQL a trailing blank does not count in `=`, so ids `'a'` and `'a '` are one key.

A typed `decimal` or `double` column holds its value exactly on every store.

## Consequences

- Behavioural: on SQL Server `Get("ABC")` no longer finds `"abc"`. Text fields, their equality and their sort are unchanged.
- Evidence: `OrdinaryValueConvergence` on SQLite, PostgreSQL, SQL Server and MySQL; `SqlServerExactValueSpec` holds DateTime ticks,
  a DateTime set and concurrent upserts.
