# Empirical Specification

*Or: how to write a protocol by not writing a protocol.*

---

## The Traditional Way

You know how this usually goes.

A committee meets. Requirements are gathered. Architects architect. A specification is written—comprehensive, detailed, theoretically complete. Then implementation begins, and the specification meets reality, and reality wins, and the specification is revised, and the revision meets reality, and reality wins again, and eventually you have a system that works and a specification that describes something slightly different.

The specification becomes archeology. "Why does it work this way?" "The spec says—" "No, I mean *actually* why." "Oh, that. We had to change it because..."

There is another way.

---

## Cultivation

Imagine you're building a garden. Not designing one—*building* one. You have stones, and a space, and a vague sense of what you want.

You place a stone. You step back. You notice how the light falls around it, how it changes the flow of the path, what it implies about where the next stone might go.

You place another stone. Step back again. The garden is telling you something now. Not what you expected, maybe. Better, in some ways. Worse in others. But *real*.

You adjust. You move a stone. You add a third. The garden emerges from the conversation between your intention and the material's response.

This is cultivation. And it's how Zen Garden's protocol is developed.

---

## The Middle Path

We are not writing a specification and then implementing it. We are not hacking without direction and calling it agile. We are doing something in between.

**Test.** Try something. Actually build it. Put it on real hardware, in real network conditions, with real constraints.

**See if it fits.** Does it work? Not in theory—actually. Does the UDP broadcast reach all the Stones? Does the election algorithm prevent reply storms? Does the cache TTL feel right in practice?

**Incorporate.** What worked becomes part of the system. What didn't becomes a lesson. The specification grows from the accumulated *yeses*—the things that survived contact with reality.

**Think holistically.** Each piece affects the others. Discovery shapes security. Security shapes state management. State management shapes failure handling. You cannot specify one without understanding its pressure on the rest.

This is slower than writing a specification upfront. It is faster than writing a specification, implementing it, discovering it's wrong, and rewriting both.

---

## Standards Over Implementations

Here is something important, and easy to miss: Zen Garden is not a Rust project that happens to have documentation. Zen Garden is a *protocol* that happens to have a Rust implementation.

The distinction matters.

A project is defined by its code. Change the language, and you have a different project.

A protocol is defined by its behavior. Change the implementation, and you have... the same protocol, implemented differently. A conforming implementation in Go, Python, C#, or shell scripts would be equally valid. It would be *Zen Garden*, not "Zen Garden compatible."

This is why the current technology choices are documented as *choices*, not requirements:

**Rust** — Because we needed performance and single-binary distribution across Windows and Linux. Because the team had Rust experience. Because the type system catches errors early. But if someone implemented Moss in Go, it would work.

**Docker Compose** — Because the team had production experience with it. Because it's sufficient for the target scale. Because it's well-understood and well-documented. But if someone orchestrated containers differently, the protocol wouldn't notice.

**mDNS** — Because it's proven (twenty years, billions of devices). Because it's zero-configuration. Because it's built into macOS and Linux. But Windows is bad at mDNS, so we *also* have UDP broadcast, and the protocol accommodates both.

**HTTP/REST** — Because it's universal. Because you can debug it with curl. Because every language has HTTP libraries. But the protocol could ride on gRPC or WebSockets or carrier pigeons if the semantics were preserved.

The implementation choices are *informed guesses*—good starting points based on experience and constraints. They are not the protocol. The protocol is what remains when you abstract away the implementation.

---

## The ZGP Specifications

At some point, the accumulated *yeses* become formal. The things that survived cultivation become specifications.

We're calling them ZGP—Zen Garden Protocol. They don't exist yet, not as finished documents. That's honest, not embarrassing. You cannot specify what you haven't yet discovered.

Here's what we expect them to contain, based on what's emerging:

**ZGP-001: Service Discovery**
How Stones announce themselves. How clients find services. The mDNS service types, the TXT record schema, the UDP broadcast fallback. This one is closest to stable—discovery has been tested most thoroughly.

**ZGP-002: Connection Strings**
The `zen-garden:<service>[/<database>]` format. How clients resolve it. Caching behavior. Failure handling. This emerges from the discovery work but has its own surface area.

**ZGP-003: Lantern API**
The HTTP registry for larger gardens. Endpoint structure. State synchronization. Election protocol for multi-active Lanterns. This is less tested than discovery—it only matters at scale we haven't fully exercised.

**ZGP-004: Pond Security**
mTLS certificate handling. TOTP admission. Keystone protection tiers. This is designed but not battle-tested. Security specifications require more caution; we're moving carefully.

**ZGP-005: Conformance**
How to verify an implementation is correct. Test vectors. Expected behaviors. This comes last, because you cannot test conformance to a specification that isn't finished.

Each ZGP will document what *actually works*, not what we *hope* will work. If a behavior changes because reality demanded it, the specification changes too. The spec follows the implementation until the implementation stabilizes; then the spec leads.

---

## What We Don't Know Yet

Intellectual honesty requires admitting uncertainty. Here's what we're still figuring out:

**Scale transitions.** The garden model works beautifully at 3-10 Stones. We believe it works at 10-50 with Lanterns. We don't know exactly where it breaks. The protocol may need revision when we find out.

**Security boundaries.** Pond's TOTP admission is elegant in theory. We haven't tested it with adversarial users. The threat model may need adjustment based on real-world attempts to subvert it.

**Federation.** The "bridges to neighbors" concept—connecting separate gardens—is speculative. We know we want it eventually. We don't know what it looks like yet.

**Template evolution.** Offering templates work now. What happens when MongoDB 8 breaks compatibility with the MongoDB 7 template? Upgrade semantics are not fully designed.

These aren't failures. They're the current edges of cultivation. The protocol will grow to address them when we understand them well enough to encode that understanding.

---

## For the Implementer

If you're reading this because you want to build a conforming implementation, here's the honest state of affairs:

**You can build against the current behavior.** The Moss daemon's API is documented. The discovery protocol is stable enough. Rake's command structure is defined. You could implement a Moss in another language and it would interoperate.

**You will be a cultivator, not a consumer.** The specification is not finished. If you implement and discover that something doesn't work—that the spec is ambiguous, or wrong, or incomplete—you're not failing to comply. You're contributing to the protocol's development.

**The reference implementation is the tiebreaker.** When the spec and the Rust implementation disagree, the Rust implementation is probably right, and the spec needs updating. This will change once the protocol stabilizes. For now, the code is the truth.

**Talk to us.** Cultivation is collaborative. If you're building something, we want to know. Your experience shapes the protocol.

---

## The Joy in This

There is a particular satisfaction in building something that works.

Not something that *should* work. Not something that *theoretically* works. Something that you have tested, adjusted, tested again, and found solid. Something you understand because you've felt its edges, not because you've read its description.

This is the joy we're offering technical readers: the chance to work on a system where the specification *means something*. Where the protocol isn't a fantasy document that the implementation ignores. Where your contribution—your bug report, your edge case, your "this doesn't work in my environment"—becomes part of the truth.

The specification emerges from cultivation. You can cultivate too.

---

## What Comes Next

The next document, **Humanist Infrastructure**, explains the *why* underneath all of this. The mission. The e-waste framing. The reason we care about joy in a field that usually doesn't.

Then the technical pillars begin: Discovery, Security, Offerings, State, Failure, Joy.

But you have the method now. When you read those pillars, you'll understand: these aren't arbitrary designs. They're what survived.

---

*Zen Garden Documentation — Foundations*
