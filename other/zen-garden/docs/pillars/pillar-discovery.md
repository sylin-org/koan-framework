# Discovery

*Or: how Stones find each other without being told.*

---

## The Weight of Moss

Before we talk about discovery, consider what's actually running on each Stone.

```
garden-moss     11.26 MB    (Linux)
garden-rake      7.29 MB    (CLI)
garden-lantern   3.15 MB    (Registry)
```

Eleven megabytes. That's the daemon that manages services, announces presence, responds to commands, monitors health, and coordinates with every other Stone in the garden.

On a machine with 2GB of RAM—the kind of "obsolete" hardware Zen Garden is designed for—you barely notice it's there. This isn't a 500MB JVM application. It isn't a container platform that needs gigabytes just to start. It's moss: a thin layer of life on stone, almost weightless, but transforming what the stone can do.

This lightness is not incidental. It's what makes discovery *possible* on hardware that was thrown away for being too weak. You cannot build infrastructure for e-waste if your infrastructure is heavier than what the e-waste can carry.

---

## The Service Binding Problem

Here's what Zen Garden actually solves.

DNS gives you hostname resolution: "db-server.local" becomes 192.168.1.50. This is useful, but it's the wrong abstraction. You don't care about *machines*. You care about *services*. You want MongoDB, not "the machine that happens to run MongoDB today."

```
DNS:           "db-server.local" → 192.168.1.50
               Machine has a name. Service lives somewhere on it.
               Machine dies → update DNS → update configurations → hope you found them all.

Zen Garden:    "zen-garden:mongodb" → whoever currently offers MongoDB
               Service has a name. Machine is incidental.
               Machine dies → move service → nothing else changes.
```

When your laptop dies and you move MongoDB to a Raspberry Pi, DNS requires updating records. Your applications have connection strings pointing to the old machine. Configuration files scattered across systems need to change. You will miss some of them. Things will break at 2 AM.

Zen Garden inverts this. Applications connect to `zen-garden:mongodb`. The Stone offering MongoDB announces itself. If the Stone changes, the announcement changes. Applications reconnect automatically. The configuration never mentioned a machine name, so there's nothing to update.

This is the service binding problem: decoupling what you *need* (a service) from where it *happens to live* (a machine). Discovery is the mechanism. Lightness is what makes it practical.

---

## Connection Strings

The syntax is intentionally simple:

```
zen-garden:<service>[/<database>]
```

Examples:

```
zen-garden:mongodb           → MongoDB, any database
zen-garden:mongodb/myapp     → MongoDB, "myapp" database
zen-garden:redis             → Redis
zen-garden:postgresql/prod   → PostgreSQL, "prod" database
```

That's it. No host. No port. No credentials in the string. Just: what do you need?

Resolution happens at connection time. The client library (or Moss, if you're on a Stone) finds who's offering that service, resolves their address, and returns a native connection string your driver understands:

```
zen-garden:mongodb/myapp  →  mongodb://192.168.1.42:27017/myapp
```

The application never sees the IP address. It asks for MongoDB and gets MongoDB. Where it lives is someone else's problem.

---

## The Discovery Cascade

Remember the question from the vocabulary: *can you see all your Stones from where you stand?*

Discovery works in layers, each reaching further than the last:

```
Method                  Reach                   Speed
─────────────────────────────────────────────────────────────
1. Localhost cache      This Stone              <1 ms
2. UDP broadcast        Local subnet            ~100 ms
3. mDNS browse          Local subnet            ~100 ms
4. Lantern query        Anywhere Lantern sees   ~200 ms
5. Manual --at          Anywhere you specify    Direct
─────────────────────────────────────────────────────────────
```

Each method is tried in order until one succeeds. Most operations never get past the first.

### Localhost Cache

If you're running Rake on a Stone (the common case), discovery is instant.

Every Moss daemon maintains a hot cache of the entire garden topology. When Stones announce themselves—which they do continuously—every other Stone hears it and updates its cache. By the time you run a command, Moss already knows where everything is.

```
$ garden-rake list
```

This doesn't discover anything. It asks the local Moss "what do you know?" and gets an immediate answer. Sub-millisecond. No network traffic.

The cache has a 90-second TTL. If a Stone goes silent for 90 seconds, it fades from the cache. If it comes back, it announces itself, and everyone learns again. The network *is* the source of truth—the cache is just memory of what the network said recently.

### UDP Broadcast

When there's no local Moss (you're running Rake from a laptop that isn't a Stone), we broadcast.

```
Rake → 255.255.255.255:7184
       "Who's out there?"

All Stones hear this. But they don't all respond at once—that would be chaos. Instead, each Stone calculates a delay based on a hash of its name and the request ID:

delay_ms = blake3(stone_name + request_id)[0] × 10

The Stone with the lowest hash responds first. By the time the others would respond, Rake already has an answer and stops listening. One request, one response, full topology—because the responding Stone shares its cache.

This works on Windows, which is the point. Windows doesn't have reliable mDNS browsing, but it handles UDP broadcast fine.

### mDNS Browse

On Linux and macOS, mDNS provides another path. Stones announce themselves as:

```
stone-01-moss._moss._tcp.local.    →  Moss daemon
stone-01-mongodb._koan-stone._tcp.local.  →  MongoDB service
```

Clients can browse these service types and discover what's available. This is the same protocol used by AirPlay, Chromecast, and Spotify Connect—proven, zero-configuration, built into the operating system.

mDNS and UDP broadcast coexist. Use whichever works in your environment. The protocol doesn't care.

### Lantern Query

When the garden grows beyond a single subnet, broadcast stops working. Radio waves don't cross routers. This is physics, not a bug.

So you light a lantern.

Lantern is a registry service—a Stone that keeps track of other Stones across network boundaries. When Moss can't reach peers directly, it registers with Lantern. When Rake can't discover locally, it asks Lantern who's out there.

```
GET http://lantern:7186/api/garden/stones
```

Lantern doesn't replace local discovery. It *extends* it. Small gardens don't need Lantern. Growing gardens add it when they need to see further.

### Manual Override

When all else fails, or when you know exactly what you want:

```
$ garden-rake status --at stone-03.local
```

This bypasses discovery entirely. You're pointing directly at a Stone. It always works, assuming the Stone is reachable.

This is the escape hatch. Discovery is convenient, but never mandatory.

---

## What Gets Announced

When a Stone offers a service, it announces what applications need to find it:

```
Service: stone-01-mongodb._koan-stone._tcp.local.
Port: 27017
TXT Records:
  offering=mongodb
  version=7.0.4
  protocol=native
  categories=database,document-database
  health=healthy
  stone_name=stone-01
```

The TXT records carry metadata. Clients can filter: "give me a healthy MongoDB" or "give me any document database." The announcement is self-describing—you can find what you need without knowing exactly what to ask for.

Health status is part of the announcement. If a service degrades, its announcement updates. Clients preferentially connect to healthy services. This isn't load balancing (Zen Garden doesn't do load balancing), but it is *health-aware routing*—don't send traffic to something that's struggling.

---

## Failure Modes

Discovery can fail. Here's what happens:

**No response (timeout).** The cascade continues to the next method. If all methods fail, Rake reports that no Stones were found and suggests checking network connectivity or using `--at` for direct access.

**Stale cache.** A Stone in the cache may have gone offline. Connections to it will fail. The client retries discovery, gets fresh data, tries again. This is automatic—applications don't see the retry, just a slightly slower connection.

**Network partition.** If the network splits, Stones can only see Stones on their side. This is correct behavior—you can't discover what you can't reach. When the partition heals, announcements resume, and the topology reconverges.

**Lantern unavailable.** If you depend on Lantern and it's down, cross-subnet discovery fails. But local discovery still works. The garden doesn't collapse; it just can't see as far until the lantern is relit.

The design principle: **degrade gracefully, never catastrophically.** Each layer fails independently. Failure of a higher layer (Lantern) doesn't break lower layers (local broadcast). Failure of discovery doesn't break direct access (`--at`).

---

## For the Implementer

If you're building a client library that resolves `zen-garden:` connection strings, here's what you need:

1. **Parse the connection string.** Extract service type and optional database.

2. **Try local Moss first.** If there's a Moss on localhost:7185, ask it. This is the fast path.

3. **Try UDP broadcast.** Send a discovery request to 255.255.255.255:7184, listen for a response on port 7185. Use the election algorithm if you want to avoid duplicates, or just take the first response.

4. **Try mDNS.** Browse for `_koan-stone._tcp.local.`, filter by the `offering` TXT record.

5. **Try Lantern.** If you have a Lantern endpoint (from config or prior discovery), query it.

6. **Cache the result.** TTL of 5 minutes is reasonable for application-level caching. Moss uses 90 seconds; applications can be lazier.

7. **Build the native connection string.** Take the IP and port you discovered, append the database if specified, return a connection string the native driver understands.

8. **Handle failure.** If the connection fails, invalidate the cache and rediscover. Don't cache failures—transient issues shouldn't poison the cache.

The reference implementation is in Rust, but the protocol is language-agnostic. A conforming implementation in Python, Go, or JavaScript would interoperate fully.

---

## The Lightness Again

Discovery works because the components are light enough to run everywhere.

If Moss were 500 MB, you couldn't run it on e-waste thin clients. If it required gigabytes of RAM, the hot cache would be a joke. If startup took minutes, the protocol would need complex liveness checks.

But Moss is 11 MB. It starts in seconds. It runs on machines with 2 GB of RAM without breaking a sweat. So every Stone can participate in discovery. Every Stone can maintain a cache. Every Stone can announce and listen and respond.

The lightness isn't a feature. It's the foundation that makes every other feature possible.

---

## What Comes Next

Discovery is how Stones find each other. But finding isn't trusting.

The next pillar, **Security (Pond)**, covers what happens when you need boundaries. When not everyone should see everything. When the garden needs a protected space, and the water changes the rules.

---

*Zen Garden Documentation — Technical Pillars*
