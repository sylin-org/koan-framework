# Understanding Zen Garden

**Automatic service discovery for self-hosted infrastructure. No hardcoded IPs. No configuration files. Physical devices that just work.**

---

## Hello World in 60 Seconds

```bash
# Terminal 1: Start a MongoDB Stone
docker run -d -p 27017:27017 --name mongo-stone \
  -e ANNOUNCE_SERVICE=mongodb \
  zen-garden/stone:latest

# Terminal 2: Your app connects instantly
CONNECTION_STRING="zen-garden:mongodb" node app.js
# Auto-discovers MongoDB, connects. Done.
```

**What just happened?**
1. Stone announced "I offer MongoDB" via mDNS broadcast
2. Your app asked "Who has MongoDB?"
3. Stone responded with its location
4. Connection established automatically

**This is the entire system.** Everything else—Lanterns, Ponds, security—is optional extension.

---

## The Problem: Self-Hosting's Coordination Tax

Traditional self-hosting works until it doesn't:

```bash
# Every config file in your stack
MONGODB_URI=mongodb://192.168.1.50:27017/mydb
REDIS_URL=redis://192.168.1.51:6379
POSTGRES_URL=postgres://192.168.1.52:5432/db

# Then the router reboots, IPs change, and you spend 
# your evening grepping for hardcoded addresses
```

You avoid self-hosting not because of cloud bills, but because **coordination work**—keeping track of where services live—scales poorly. IP addresses change, services move, configurations drift.

**Zen Garden eliminates this:**

```bash
# Never changes, no matter where services move
MONGODB_URI=zen-garden:mongodb/mydb
REDIS_URL=zen-garden:redis
POSTGRES_URL=zen-garden:postgres/db
```

Services announce themselves. Apps discover services by intent. Infrastructure becomes **intent-based** instead of **location-based**.

---

## Classroom Demonstration

*A university computer lab. Students plug in three devices with colored labels: blue (Database), green (Storage), orange (Compute).*

**Terminal 1 (Lantern running):**
```
[lantern] stone joined: db-stone-01 (mongodb)
[lantern] stone joined: storage-stone-01 (minio)  
[lantern] stone joined: compute-stone-01 (docker)
```

**Terminal 2 (Student's laptop):**
```bash
# Run the app with intent-based discovery
APP_DB=zen-garden:mongodb APP_FILES=zen-garden:storage dotnet run

# Output
[resolver] mongodb -> mongodb://db-stone-01:27017
[resolver] storage -> http://storage-stone-01:9000
[app] connected to database
[app] uploaded file: test-image.jpg
[app] ready at http://localhost:5000
```

**The teacher unplugs the blue stone. Students gasp.**

**Plugs in a different blue stone labeled "db-stone-02":**
```
[lantern] stone left: db-stone-01
[lantern] stone joined: db-stone-02 (mongodb)
[resolver] mongodb -> mongodb://db-stone-02:27017
[app] reconnected to database
```

**The app never changed. No config files edited. No IPs updated. Infrastructure became physical, visible, swappable.**

This is the value proposition: **services are devices you can hold, swap, and own**.

**A garden is different**: Stones rest where placed. Lanterns reveal what is there. The pond holds everything together. When seasons change (networks shift, hardware moves), the garden adapts—stones re-announce, lanterns update, connections flow anew.

You don't fight your garden. You tend it.

---

## The Natural Order

### A Garden Contains Stones

In a zen garden, **stones rest where placed**. They do not create the garden—they inhabit it. The garden gives them context; they give the garden substance.

**In Zen Garden**:

- **Stones** offer services (MongoDB, PostgreSQL, files)
- **Lanterns** illuminate what stones offer (service directory)
- **The garden** is simply this: stones arranged, lantern guiding, harmony emerging

### Lanterns Illuminate What Is There

"Master," the student asked, watching the lantern's glow, "what makes a lantern different from a stone?"

The Master gestured to the light spreading across the garden. "A stone offers a service—'I am MongoDB, come store your data.' Stones can speak directly to each other, peer to peer. But how do you find them easily? The lantern **illuminates**—it maintains awareness of every stone, their locations, their offerings. When you ask 'where is MongoDB?', the lantern answers immediately."

"So the lantern is optional?"

"Yes. Stones can discover each other through broadcast—calling out 'who offers MongoDB?' and listening for answers. This works. But a lantern makes discovery instant—a living map. The stones announce themselves, the lantern records their positions. When networks shift, when stones move, the lantern updates. The map breathes with the garden's changes."

> **Key Insight**: Lanterns are optional. Gardens work peer-to-peer without them. Lanterns add speed, not requirement.

**In Zen Garden**:

- **The Lantern** (optional) illuminates your infrastructure (shows which Stones offer which services)
- It maintains awareness (service directory: "Stone-01 offers MongoDB at this location")
- It guides connections (when apps ask "where is MongoDB?", Lantern answers instantly)
- Without a Lantern, Stones discover each other peer-to-peer (broadcast queries, direct responses)

The Master touched the lantern's surface. "Technically: Stones broadcast service announcements via mDNS. With a Lantern present, it maintains a central directory for instant lookups. Without a Lantern, devices query via UDP broadcast and Stones respond directly. Both work—Lantern adds speed and convenience."

### The Pond: Optional Depth

"Master," the student asked, kneeling beside the raked gravel, "must every garden have a pond?"

"No." The Master's answer was immediate. "Most zen gardens have no pond—just stones, raked gravel, perhaps moss. Simple. Peaceful. Easy to tend. This is **karesansui**, the dry landscape. Sufficient for beauty, sufficient for harmony."

"Then why would I add water?"

The Master pointed to a garden across the courtyard, where a small pond reflected the sky. "When you need **depth**. When you need **protection**. A pond with stones beneath its surface—the lantern's light reaches the water, you see reflections, ripples, but the stones themselves are hidden. To touch them, you must enter the water."

"I don't understand, Master."

"Without a pond, your stones rest on gravel—visible, accessible, anyone can reach them. With a pond, stones rest beneath the surface—hidden behind cryptography. To reach them, you must authenticate, prove you belong. The water protects what lies beneath."

**In Zen Garden**:

- **No Pond** (default): Stones rest on gravel, visible to all, free to move. Simple garden. Perfect for learning.
- **Pond Added** (security mode): Stones rest beneath water, hidden behind cryptography. To reach them, authenticate—"get wet"

The Master drew a breath. "Technically: Secure Pond mode uses Ed25519 key pairs. The lantern generates a pond master key, creates encrypted pebbles for each stone. Stones store these pebbles in a 4MB partition—AES-256-GCM encryption. When an app connects, it must authenticate with the pond key. Without it, the stone appears present but inaccessible. Like seeing a stone's shadow beneath water, but unable to touch it without diving in."

> **Key Insight**: The pond is optional. Default = no pond (simple, playground). Add pond later when you need security (production, sensitive data, theft protection). Start dry, add water when needed.

**A pond is a choice**: Start simple (no pond). Add water later (when you need security).

### Pebbles Carry Memory

"Master," the student asked, running fingers through the raked gravel, "what are these patterns?"

The Master knelt, drawing a finger through the gravel, creating a deliberate wave pattern. "In a zen garden, pebbles are gravel raked into patterns—each pattern unique to that garden, each stroke intentional. These patterns are **memory**."

"Memory of what?"

"Memory of belonging." The Master pointed to a stone. "This stone, virgin, carries no pattern. It can join any garden, rest anywhere. But when I rake the gravel around it"—their hand swept in careful arcs—"I create a pattern unique to this garden. The stone now **remembers** where it belongs."

The student touched the newly raked pattern. "But these are just lines in gravel. The wind could erase them."

"In gravel, yes. But in your infrastructure, patterns are cryptographic. A pebble is a stone's memory—encrypted, persistent, indelible."

The raked gravel in a zen garden—each pattern unique, created intentionally, preserved carefully. **Patterns are memory**.

**In Zen Garden**:

- **Pebbles** are cryptographic memories (Stone remembers which pond it belongs to)
- Virgin Stones have no pattern (can join any pond)
- Bound Stones carry their pattern (remember their pond, cannot forget without factory reset)

The Master brushed gravel from their hands. "A pebble is a 4MB encrypted partition on the stone's storage. It contains Ed25519 keys—the pond's public key, the stone's private key. When the stone boots, it reads its pebble: 'I belong to pond-abc123.' It will only serve requests authenticated with that pond's keys. Factory reset destroys the pebble. The stone forgets. It becomes virgin again, ready for a new pattern."

---

## The Creation Flow (As a Garden Forms)

### Nothing → Stone → (Optional: Lantern) → More Stones → (Optional: Pond)

**The Empty Space** (Before the garden exists)

```
You have devices, but no garden.
Like stones lying in a field—potential, not yet arranged.
```

**The First Stone** (The garden begins)

```
You place a stone.
The stone settles, announces its nature: "I am MongoDB."
The garden exists—simple, complete.

Action: Install Stone software, announce MongoDB service
Result: Your garden begins (one stone, peer-to-peer discovery)
```

**Optional: Add a Lantern** (Light reveals)

```
You add a lantern.
The lantern hears the stone's announcement, records it.
Discovery becomes instant—no broadcast queries needed.

Action: Install Lantern software on a device
Result: Instant service discovery (Lantern maintains directory)
```

**More Stones** (The garden grows)

```
You add another stone—PostgreSQL, Redis, files.
Each announces itself (to Lantern if present, or via broadcast).
The garden grows naturally.

Action: Add more Stone services
Result: Multi-service infrastructure (auto-discovery throughout)
```

**The Connection** (Discovery flows)

```
An app approaches the garden, seeking MongoDB.

With Lantern:
  App asks the lantern: "Where is MongoDB?"
  Lantern answers instantly: "Stone-01, there."
  App connects directly to Stone.

Without Lantern:
  App broadcasts: "Who offers MongoDB?"
  Stone-01 hears, responds: "I do, here's my location."
  App connects directly to Stone.

Both work. Lantern makes it faster.

Action: App uses connection string "zen-garden:mongodb"
Result: Auto-discovery guides the app to Stone-01
```

> **Developer Note**: Both discovery methods work identically from your app's perspective. Connection string `zen-garden:mongodb` abstracts the discovery mechanism. With Lantern = instant lookup. Without Lantern = broadcast query. Your code doesn't change.

**The Binding** (Optional: Memory etches)

```
You choose permanence—Secure Pond mode.
The lantern creates pebbles (cryptographic patterns).
Each stone receives its pebble: "Remember this pond."
The pattern etches into the stone's memory.

Action: Enable Secure Pond on Lantern web UI
Result: Stones bind to this pond (cannot work elsewhere without forgetting—factory reset)
```

**The Guardian** (Optional: Protection offsite)

```
You place a sentinel stone beyond the garden—far away, watching.
The sentinel receives encrypted reflections of the garden each night.
If the garden burns, the sentinel remembers everything.

Action: Configure Backup Stone, place at parent's house
Result: Offsite disaster recovery (garden survives destruction)
```

---

## Relationships in the Garden

### Garden to Lantern

**The lantern reveals what exists.**

- A garden can exist without a Lantern (stones discover each other peer-to-peer)
- When present, a Lantern provides instant discovery (service directory)
- Multiple Lanterns can illuminate the same garden (redundancy: if one lantern fails, others continue)
- No Lantern simply means slower discovery (broadcast queries instead of instant lookup)

**A Lantern adds convenience, not requirement** (like a garden path—helpful, but you can still walk through grass).

---

### Stone to Garden

**A stone rests within the garden.**

- Stone announces itself via mDNS broadcast ("I am here, I offer MongoDB")
- If a Lantern is present, it hears the announcement and records it
---

## Three Core Concepts

### Stones (Devices That Offer Services)

A **Stone** is any device running a service—MongoDB, PostgreSQL, Redis, file storage.

**Characteristics:**
- Announces itself via mDNS: "I offer MongoDB"
- Serves when discovered
- No configuration required

**Physical form:** Old laptop, Raspberry Pi, compute module, recycled hardware.

**Example:**
```bash
# Run on device
docker run -d -p 27017:27017 \
  -e ANNOUNCE_SERVICE=mongodb \
  zen-garden/stone:latest

# Service now discoverable as zen-garden:mongodb
```

**Key insight:** Stones are self-announcing. Plug in, boot, serve. No IP addresses to track.

---

### Lantern (Optional Directory)

A **Lantern** tracks which Stones offer which services—like a phonebook for your infrastructure.

**Characteristics:**
- Listens for Stone announcements
- Maintains service directory
- Provides instant discovery (faster than peer-to-peer broadcast)
- Shows web dashboard of garden state

**When to use:**
- Gardens with 3+ Stones (faster discovery)
- When you want visibility into infrastructure
- Production deployments (redundant Lanterns for reliability)

**When to skip:**
- Single Stone setups
- Learning/experimentation
- Peer-to-peer discovery sufficient (<100ms)

**Physical form:** Any device with network access—Pi, old laptop, dedicated server.

**Key insight:** Lanterns are optional convenience. Stones work peer-to-peer without them.

---

### Pond (Optional Security Layer)

A **Pond** adds cryptographic binding—Stones only serve authenticated requests.

**Characteristics:**
- Ed25519 key pairs for each Stone
- AES-256-GCM encryption for service traffic
- Pebbles (4MB partitions) store Stone identity
- Factory reset destroys pebbles (Stone becomes "virgin" again)

**When to use:**
- Production deployments with sensitive data
- Physical theft protection (stolen Stone won't serve without pond key)
- Multi-tenant infrastructure (isolate services)

**When to skip:**
- Home labs, learning environments
- Personal projects without sensitive data
- Development/testing setups

**Key insight:** Default is no Pond (simple playground). Add security when needed, not before.

---

## Architecture: How Discovery Works

### Without Lantern (Peer-to-Peer)

```
1. App broadcasts: "Who offers MongoDB?"
2. Stone responds: "I do, at 192.168.1.50:27017"
3. App connects directly to Stone
```

**Latency:** <100ms (local network)  
**Reliability:** Works if Stone is reachable  
**Use case:** 1-2 Stone setups, simple discovery

---

### With Lantern (Directory-Based)

```
1. Stone announces: "I offer MongoDB" → Lantern records
2. App queries Lantern: "Where is MongoDB?"
3. Lantern responds: "Stone-01 at 192.168.1.50:27017"
4. App connects directly to Stone
```

**Latency:** <10ms (Lantern local cache)  
**Reliability:** Redundant Lanterns for failover  
**Use case:** 3+ Stones, production infrastructure, dashboard visibility

---

### With Pond (Secure Mode)

```
1. Stone announces with encrypted identity
2. App queries Lantern with pond key
3. Lantern validates pond membership
4. App receives Stone location + auth token
5. App connects to Stone with mutual TLS
```

**Latency:** <50ms (includes crypto handshake)  
**Reliability:** Stolen Stones unusable without pond key  
**Use case:** Production, sensitive data, physical security requirements

This is perfect for learning, testing, homelab experimentation. Play freely.

**But you can add water** (Secure Pond mode):

#### Adding a Pond (守られた池 - Protected Pond)

**You choose to add security**—stones now rest beneath the surface.

- Lantern creates cryptographic pebbles (identity tokens)
- Each Stone receives a pebble (binds to this garden)
- Stones submerge (hidden behind authentication)
- To access a stone, prove you belong ("get wet"—authenticate)
- Stolen stones are useless (factory reset required, destroys data)

**When to add water**:

- Production data (not just testing)
- Sensitive information (privacy required)
- Theft protection (physical security concern)
- Compliance requirements

The student watched the Master's hands move through the gravel. "So the pond is... a choice?"

"Always a choice. Start simple—dry garden. Add water only when you need protection."

------

## Deployment: From Discovery to Running Apps

Discovery is half the story. **How do you actually run applications on Stones?**

### garden-rake (Deployment Tool)

**garden-rake** pushes containers to compute Stones automatically.

```bash
# Deploy app to any available compute Stone
garden-rake push myapp --image myapp:latest

# Output
[rake] selecting compute stone... compute-stone-01
[rake] pulling image... ok
[rake] starting container... ok
[rake] app live at: http://myapp.garden/
```

**What it does:**
1. Discovers all compute Stones via `zen-garden:docker`
2. Selects Stone with available resources (CPU, RAM, disk)
3. Pushes container image
4. Starts container with auto-restart
5. Configures mDNS for `myapp.garden` hostname

**Complete workflow example:**

```bash
# Terminal 1: Lantern running
[lantern] stone joined: db-stone-01 (mongodb)
[lantern] stone joined: storage-stone-01 (minio)
[lantern] stone joined: compute-stone-01 (docker)

# Terminal 2: Deploy app with environment
garden-rake push webapp \
  --image webapp:latest \
  --env APP_DB=zen-garden:mongodb \
  --env APP_FILES=zen-garden:storage

# Output
[rake] resolved: zen-garden:mongodb -> mongodb://db-stone-01:27017
[rake] resolved: zen-garden:storage -> http://storage-stone-01:9000
[rake] deploying to: compute-stone-01
[rake] container started: webapp-a7f3
[rake] app live at: http://webapp.garden/
```

**Key benefits:**
- No Kubernetes complexity
- No YAML manifests
- Intent-based deployment: "run this app on any available compute Stone"
- Apps automatically get discovery-based environment variables

See [REFERENCE.md](REFERENCE.md#garden-rake-deployment) for full API documentation.

---

## Complete Example: Three-Stone Garden

**Hardware:**
- Lantern: Raspberry Pi 4 ($35)
- Database Stone: Old laptop with SSD
- Storage Stone: Pi + 1TB USB drive
- Compute Stone: Intel NUC

**Setup (5 minutes):**

```bash
# On Lantern device
curl -sSL https://get.zen-garden.dev | bash
sudo systemctl enable --now zen-garden-lantern

# On each Stone
curl -sSL https://get.zen-garden.dev/stone | bash

# Database Stone
docker run -d -p 27017:27017 \
  -e ANNOUNCE_SERVICE=mongodb \
  -v /data/mongodb:/data/db \
  mongo:latest

# Storage Stone
docker run -d -p 9000:9000 \
  -e ANNOUNCE_SERVICE=storage \
  -v /data/minio:/data \
  minio/minio:latest

# Compute Stone
# (Just runs stone software, accepts deployments)
```

**Usage:**

```bash
# Deploy app
garden-rake push notes-app \
  --image notes:latest \
  --env DB=zen-garden:mongodb

# App connects automatically
# Available at: http://notes-app.garden/
```

**What happens when database Stone dies?**

```bash
# Swap failed Stone with new hardware
# Boot Stone, run:
docker run -d -p 27017:27017 \
  -e ANNOUNCE_SERVICE=mongodb \
  -v /backup:/data/db \
  mongo:latest

# Automatic announcement
[lantern] stone joined: db-stone-02 (mongodb)

# Apps reconnect automatically
[notes-app] database connection lost
[notes-app] discovering: zen-garden:mongodb
[notes-app] resolved: mongodb://db-stone-02:27017
[notes-app] reconnected
```

**Zero configuration changes. Physical infrastructure as code.**

"And if the garden burns?" the student asked quietly. "If everything is destroyed?"

The Master turned, pointing to the horizon. "Ten miles from here, at my teacher's monastery, there is a stone. Every night, it receives encrypted reflections of this garden—snapshots of all data, a shard of the pond key. If this garden burns to ash, I retrieve that sentinel stone. I bring it home, run the recovery command. Within hours, the garden is reborn."

"The stone remembers?"

"The stone preserves. Memory offsite, waiting."

```
Backup Stone at parent's house (offsite).

Normal: Receives encrypted snapshots nightly.

Disaster strikes (fire, flood, theft):
  Retrieve Backup Stone
  Run recovery command
  Garden restored

Memory preserved offsite. Garden reborn.
```

---

---

## When to Use Zen Garden

**Good fit:**
- Personal projects (homelabs, self-hosted apps)
- Small business infrastructure (5-50 devices)
- Privacy-sensitive workloads (healthcare, education, legal)
- Edge deployments (retail, manufacturing, remote sites)
- Development environments that mirror production
- Learning infrastructure concepts hands-on

**Not a fit:**
- Global-scale services (multi-region replication)
- Sub-10ms latency requirements (Zen Garden adds <100ms discovery)
- Existing Kubernetes investments (migration overhead not worth it)
- Teams without physical hardware access (cloud-only organizations)

---

## Comparison Matrix

| Feature | Cloud (AWS/GCP) | Kubernetes | Docker Compose | Zen Garden |
|---------|----------------|------------|----------------|------------|
| **Configuration** | Complex | Very Complex | Simple | Zero |
| **Discovery** | DNS (Route53) | Services | Manual IPs | Automatic |
| **Cost** | $500-5000/mo | $0 + labor | $0 + labor | $0 + labor |
| **IP Changes** | Handled | Handled | Breaks | Handled |
| **Learning Curve** | Weeks | Months | Hours | Minutes |
| **Physical Ownership** | No | Optional | Yes | Yes |
| **Scale** | Global | Large | Small | Small-Medium |

**Key differentiator:** Zen Garden is the only system where you plug in hardware, boot it, and services automatically become available—no YAML, no IP addresses, no DNS configuration.

---

## Failure Scenarios (What Actually Breaks)

### Stone Dies

**Symptom:** App can't connect to `zen-garden:mongodb`

**Diagnosis:**
```bash
# Check Lantern
curl http://lantern.local/api/stones
# Shows: db-stone-01 offline

# Check network
ping db-stone-01.local
# No response
```

**Fix:** Replace Stone hardware, restore from backup, services auto-reconnect.

---

### Lantern Dies (With Redundancy)

**Symptom:** None (secondary Lantern takes over)

**What happens:**
1. Primary Lantern stops responding
2. Apps query secondary Lantern (<10ms failover)
3. Discovery continues uninterrupted

---

### Lantern Dies (No Redundancy)

**Symptom:** Discovery slower (falls back to peer-to-peer broadcast)

**What happens:**
1. Apps can't reach Lantern
2. Fall back to mDNS broadcast: "Who offers mongodb?"
3. Stones respond directly
4. Latency: 10ms → 100ms (still works)

---

### Network Partition

**Symptom:** Some Stones unreachable

**What happens:**
- Apps discover only reachable Stones
- Partitioned Stones still serve local requests
- When partition heals, Stones re-announce
- Lantern updates directory automatically

**Key insight:** Zen Garden degrades gracefully. No single point of failure.

---

## Next Steps

**Start simple:**
1. [Install your first Stone](GETTING-STARTED.md) (5 minutes)
2. Connect an app with `zen-garden:mongodb`
3. Observe auto-discovery working

**Add complexity only when needed:**
- 3+ Stones → Add Lantern (instant discovery)
- Sensitive data → Enable Pond (security)
- Production deployment → Add backup Stones (resilience)

**Learn more:**
- [Technical Reference](REFERENCE.md) - Full API documentation
- [Hardware Guide](HARDWARE.md) - Recommended devices
- [Security Deep Dive](SECURITY.md) - Pond cryptography details

---

**Philosophy:** Start with one Stone. Watch it work. Add only what you need. Completeness through sufficiency, not complexity.

### Level 1: Hello World (Week 1)

**You Learn**:

- What a Stone is (device offering a service)
- What a Lantern is (coordinator showing what's available)
- How auto-discovery works (`zen-garden:mongodb` → finds Stone automatically)

**Mental Model**: "It's like magic DNS that actually works"

---

### Level 2: Multi-Service Garden (Month 1)

**You Learn**:

- Adding multiple Stones (MongoDB + PostgreSQL + Redis)
- How Lantern maintains the service map
- Why connection strings never need hardcoded IPs

**Mental Model**: "My homelab feels like AWS—services just appear when I need them"

---

### Level 3: Secure Pond (Month 3)

**You Learn**:

- Regular Pond vs Secure Pond trade-offs
- How Stone binding works (pebbles, cryptographic identity)
- Why stolen Stones are useless (binding makes data inaccessible)

**Mental Model**: "My data is safer than Synology—thieves get expensive bricks"

---
---

## FAQ: What People Actually Ask

**Q: Does this require Koan Framework?**  
A: No. Zen Garden works with any language/framework—Node.js, Python, Go, Java, Ruby. Koan provides zero-config integration, but the generic HTTP API works everywhere.

**Q: My colleagues use Kubernetes. Should I switch?**  
A: Different scales. Kubernetes = 100+ servers, enterprise complexity. Zen Garden = 3-20 devices, personal/small business scale. Don't use an imperial garden when you need a backyard.

**Q: Why not just use Synology/TrueNAS?**  
A: Those are vendor appliances—convenient but locked down. Zen Garden is infrastructure primitives—you own everything, build what you need, no vendor lock-in.

**Q: What if I don't want bound Stones?**  
A: Don't add a Pond. Keep your garden "dry"—simple plug-and-play. Add security only when needed (production, sensitive data).

**Q: What actually breaks when Stones die?**  
A: Apps can't connect to that specific Stone. Discovery still works—apps query for alternatives. Replace hardware, restore from backup, services reconnect automatically. See [Failure Scenarios](#failure-scenarios-what-actually-breaks).

**Q: How much does this cost?**  
A: $0-300 depending on hardware choices. Repurpose old laptops ($0), buy Raspberry Pis ($35-120 each), or use compute modules ($150). No monthly fees.

**Q: Can I use this for production?**  
A: Yes, with Pond security enabled and redundant Lanterns. Many small businesses run critical apps on 5-10 Stones. Not suitable for global-scale services.

---

**Related Documents:**
- [Getting Started](GETTING-STARTED.md) - Deploy your first Stone in 5 minutes
- [Technical Reference](REFERENCE.md) - Full API documentation  
- [Hardware Guide](HARDWARE.md) - Recommended devices and builds
- [Security Details](SECURITY.md) - Pond cryptography explained
- [Strategy Guide](STRATEGY.md) - Business case and ROI

**Updated:** January 15, 2026
