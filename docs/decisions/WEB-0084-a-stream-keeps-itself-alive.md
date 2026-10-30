---
id: WEB-0084
slug: a-stream-keeps-itself-alive
domain: Web
status: Accepted
date: 2026-10-30
title: A Server-Sent Events stream keeps itself alive, and a hub fans out to bounded queues
related:
  - AI-0037
  - WEB-0083
---

# WEB-0084: A Server-Sent Events stream keeps itself alive, and a hub fans out to bounded queues

## Context

`Koan.Web.Sse` wrote frames and nothing else. Its first serious consumer, Tangent Space, hand-wrote what any long-lived
stream needs: a heartbeat written once a stream has been silent for 15 seconds, a lifetime, resume from `Last-Event-ID`,
per-reader and overall stream limits, and a bounded queue per stream where a slow reader is closed rather than waited on.
None of that is about Tangent.

Its formatter also split data only on LF, while a reader's parser ends a line at CR, LF or CRLF, so a bare CR in a text
frame lost what followed it. And event names, ids and comments were written unchecked, so a value holding a line break
began another field and could forge a frame.

## Decision

1. **Every stream writes a heartbeat.** `KoanSseOptions.Heartbeat` (15 seconds, zero or less for none) is on by default
   and `Sse.Stream(..., heartbeat:)` overrides it. A comment is harmless to any conforming reader, so it needs no opt-in.
   It goes out when nothing has been written for that long, whatever arrived. `SseResult` holds the source's read across
   the wait rather than cancelling it, so a quiet source loses nothing and is written as an ordinary iterator.

2. **`lifetime` bounds a stream.** `Sse.Stream(..., lifetime:)` ends it with a normal completion for the reader to
   reconnect. Unset, it lasts until the reader leaves or the source ends. However a stream ends, the source's token is
   cancelled, its read awaited and its enumerator disposed.

3. **`Sse.LastEventId(request)` is the resume hook.** It answers what a reconnecting reader sent, or null. What to replay
   from that position is the application's record, whose shape Koan cannot know, so Koan holds no replay buffer.

4. **`SseHub<T>` fans out to bounded queues in one process.** The module registers it as an open-generic singleton.
   `Open(reader, abort)` admits a stream or returns null beyond `Hub:PerReader` (6) or `Hub:InAll` (512). `Publish` never
   waits; a stream whose queue already holds `Hub:QueueBound` (256) values is closed alone, its queue completed, its place
   returned and its request aborted, to reconnect and resume. These bounds stay because each open stream costs a queue and a
   held request: they size memory, and the application decides what a refused reader is told.

5. **The wire cannot be forged from text.** CR, LF and CRLF in data each begin a new `data:` line. A line break in an
   event name, id or comment throws `ArgumentException`, ending the stream and surfacing.

## Consequences

- Every existing stream, AI and MCP HTTP transports included, now sends a comment after 15 quiet seconds. The MCP
  server-push stream that writes outside `SseResult` is unchanged.
- `Sse.Stream` gains two optional parameters, and `SseHub<T>`, `SseConnection<T>`, `SseHubOptions`, `Sse.LastEventId` and
  `KoanSseOptions.Heartbeat`/`Hub` are added.
- A consumer that hand-wrote these replaces its beat with the default, its lifetime with the parameter, its header read
  with `Sse.LastEventId`, and its stream admission and queues with `SseHub<T>`, keeping its own frames and position.

Possibilities named and not built:

- The hub spans one process. A fleet delivers to each process's hub through Communication, and each stream replays from the
  application's record.
- A source that ignores its cancellation token holds the request open after its reader left, as `await foreach` did.

## Verification

- `SseWireSafetySpec` (Koan.Web.Sse.Tests): CR, LF and CRLF split data, and a line break in an event name, id or comment is
  refused. Nine of its ten cases failed before the change.
- `SseLivenessSpec` (Koan.Web.Sse.Tests): heartbeats through a quiet source with the held read still answering, the
  configured value and zero, no beat on a stream that keeps writing, a lifetime and a departing reader each releasing the
  source, a failing source surfacing, `Last-Event-ID`, the hub's admission limits, fan-out in order, a slow stream closed
  alone with its place returned once, a limit below one refused naming its key, and the module offering a hub for any type.
  These exercise surface that did not exist, so they could not compile before the change.
