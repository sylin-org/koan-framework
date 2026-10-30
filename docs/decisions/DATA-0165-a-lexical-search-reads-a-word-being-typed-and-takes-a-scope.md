---
id: DATA-0165
slug: a-lexical-search-reads-a-word-being-typed-and-takes-a-scope
domain: Data
status: Accepted
date: 2026-10-30
title: A lexical search reads a word being typed and takes a scope the store applies
related:
  - ARCH-0135
  - DATA-0159
---

# DATA-0165: A lexical search reads a word being typed and takes a scope the store applies

Amends [DATA-0159](DATA-0159-lexical-search-over-declared-entity-text.md): the accessor's declaration gains two knobs.

## Context

Measured at `2f008c1e1` by a consumer building search with suggestions, over readers who may not view every row.

- Every term was whole. `Post.Lexical.Search("lant", …)` found nothing where a row held "lanterns", so a search box
  answering as someone types stayed silent until the word was complete.
- The facet and both stores already took a scope and applied it before the page, but `Data.LexicalSearch` passed null
  and the accessor offered no way to name one. Unscoped, a row the reader may not view ranked first; judging the page
  afterwards leaves it short, and a short page says other rows matched. The only way to a scope was casting the routed
  repository to its facet, the ceremony an application should never write.

## Decision

1. **`AsTyped()` reads the text as still being typed.** The term the text ends in also matches as the start of a word;
   every other term stays whole. Text that ends in anything but a letter or digit holds no such word, since the word
   before it is finished. `LexicalQuery.Typing` carries it. SQLite marks it with FTS5's `*` outside its quotes;
   PostgreSQL reads the query with `to_tsquery`, the term as `term:*`, since `websearch_to_tsquery` has no prefix.
   Terms hold letters and digits only, so neither grammar can read one as an operator.
2. **The stemmer's limit is accepted.** A store stems a partial word as it stems any word, before comparing. Where its
   stem is not the start of the stored one, it finds nothing until the word is whole: "runnin" against "running", stored
   as "run", on both stores, where "lant" finds "lanterns". Words that stem to themselves, most of them, are found from
   their first letters. Serving every partial word would take a second, unstemmed index for every declaring entity; a
   person who keeps typing reaches the whole word at the next letter or two. A language without a stemmer (`simple`) has
   no such gap.
3. **`Within(predicate)` names the scope.** `LexicalSearchQuery<TEntity>` is generic so the predicate is the one
   `Entity.Query` takes: `s.Within(post => viewable.Contains(post.TangentId) && !post.Removed)`. It lowers through
   `LinqFilterCompiler`; named twice, a row answers both. The facade joins it to the managed read scope, as it did for
   the managed scope alone, so it narrows and never widens.
4. **A scope the store cannot run is refused.** The store ranks and pages what it is told, so the facade splits the
   scope by the adapter's `FilterSupport` and refuses a residual with a `NotSupportedException`. Applying one after the
   page would give short pages and disclose that other rows matched.

## Compatibility

Additive for callers of the accessor, whose lambdas infer the generic declaration. `LexicalSearchQuery` is now
`LexicalSearchQuery<TEntity>`, and `Data<TEntity,TKey>.LexicalSearch` takes the scope before the cancellation token.

A prefix read has no prefix index to serve it: the store walks the terms it covers and ranks every match, so a one-letter
prefix over a large table costs more than a whole word. An application answering each keystroke waits for a second
letter.

## Evidence

- `LexicalTypingSpec` (Koan.Tests.Data.Core): the word being typed is the one the text ends in, none after a space or
  punctuation and none on a query not declared as typed; a term repeated in another case is that word.
- `SqliteLexicalSearchSpec` (Connector.Sqlite): "lant" is found only as typed, from each declared field, in any case,
  with its word marked in the snippet; an earlier word stays whole; a trailing space ends the word; "runnin" finds
  nothing and "running" finds the row. Within a predicate, two pages are full of the rows it holds for though others
  outrank them; a set's `Contains`, its negation, a boolean field and two `Within` all hold, on `Search` and `Find`; a
  predicate the store cannot run is refused.
- `PostgresLexicalSearchSpec` (Connector.Postgres, container): the same as typed, snippet and stemmer outcomes, and the
  same two pages within a predicate.
