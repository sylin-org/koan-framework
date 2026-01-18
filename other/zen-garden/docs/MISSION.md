# Mission

**Why Zen Garden exists.**

---

## The Problems

### Problem 1: E-Waste Crisis

**62 million tonnes of electronic waste in 2024** (UN Global E-Waste Monitor)

- Growing at 2.6M tonnes/year
- Only 22% formally recycled
- Most functional devices discarded prematurely
- Average lifespan: 3-5 years (could be 8-12 years with repurposing)

**Why devices get discarded:**
1. "Too slow" for primary use (but sufficient for background services)
2. Outdated OS/software support (but hardware still functional)
3. Upgrade cycles driven by marketing (not actual failure)
4. No clear alternative use case

**Zen Garden reframes obsolete as repurposable:**
- 2015 laptop → MongoDB Stone (sufficient for 1,000+ req/sec)
- Thin client → Redis cache (low power, always-on)
- Old desktop → Storage Stone (large drives still useful)

**Impact potential:** 10,000 repurposed devices = 40-60 tonnes prevented from landfills over 3-5 years extended lifespan.

---

### Problem 2: Self-Hosting Coordination

**Current barriers to self-hosting:**

**Configuration complexity:**
```bash
# Update hardcoded IPs when router reboots
MONGODB_URI=mongodb://192.168.1.50:27017
REDIS_URL=redis://192.168.1.51:6379
POSTGRES_DSN=postgresql://192.168.1.52:5432/db

# When router reassigns IPs → everything breaks
```

**Infrastructure opacity:**
- Remote servers invisible, debugging by tunneling
- Logs scattered across cloud dashboards
- No physical feedback (can't touch, move, see)

**Monthly costs:**
- Cloud database: $25-100/month
- Cloud storage: $15-50/month
- Cloud compute: $50-200/month
- **Total: $90-350/month = $1,080-4,200/year**

**Alternative (self-hosting):**
- Old laptop (free, already owned)
- Electricity: ~$2-5/month
- Total: $24-60/year (95% cost reduction)

**Zen Garden removes coordination barrier:**
- No IP addresses to manage (`zen-garden:mongodb` never changes)
- Physical infrastructure (point at blue device = database server)
- Zero monthly fees (use owned hardware)

### The Material Constraint Reality

**Cloud mental model: infinite elasticity**
```bash
# Cloud assumption
aws ec2 run-instances --count 100
# "Need more compute? Just scale. Budget is the only limit."
```

**Self-hosting reality: finite hardware**
```bash
# Zen Garden reality
garden-rake scale webapp to 10
# "You have 3 stones. Max capacity: 6 replicas."
```

**This is not a bug—it's honesty about material constraints:**

Users self-host because:
- They **own** limited hardware (3-5 repurposed devices, not 300 VMs)
- They **can't** spawn infinite compute (no credit card for on-demand scaling)
- They **choose** finite infrastructure (environmental, economic, sovereignty reasons)

**Cloud tooling (Kubernetes, Docker Swarm) assumes:**
- Homogeneous nodes (same CPU, RAM, network)
- Elastic capacity (add nodes on-demand)
- Disposable infrastructure (kill node, spawn replacement)

**Zen Garden acknowledges:**
- Heterogeneous hardware (2015 laptop + thin client + old desktop)
- Fixed capacity (the 3 devices you own)
- Precious hardware (laptop dying = need to carefully migrate, not delete)

**Operations reflect physical reality:**
- `replace old-stone` = swap failing laptop before it dies
- `retire safely` = responsibly end-of-life hardware (wipe data, recycle)
- `cordon` = acknowledge device flakiness (overheating, disk errors)
- `lift` = planned maintenance (need to move device to different room)

**This makes Zen Garden** ***more useful*** **for its target users, not less.** Tools that pretend hardware is infinite don't serve people with finite resources.

---

## Dual Environmental Benefit

**1. Direct: Less e-waste**
- Extend device lifespan 3-5 years
- Repurpose instead of recycle (recycling still requires energy/resources)
- Delay manufacturing demand (new laptop = ~300kg CO2e)

**2. Indirect: Reduce cloud footprint**
- Data centers: 1-2% global electricity consumption
- Cloud cooling: massive water usage (evaporative cooling towers)
- Network transport: data transmission energy costs

**Local-first computing:**
- Zero network hops (LAN latency: <1ms vs cloud: 50-200ms)
- Zero cooling infrastructure (ambient room temperature sufficient)
- Zero transport energy (data stays on-premise)

**Carbon avoided (estimated):**
- Repurposed device: ~300kg CO2e (avoided manufacturing)
- Self-hosted workload: ~50-100kg CO2e/year (avoided cloud infrastructure)
- 10,000 devices: ~3,500 tonnes CO2e over 5 years

---

## Social Equity Dimension

### Accessibility

**Current self-hosting requires:**
- Networking expertise (IP routing, DNS, firewall rules)
- System administration (Linux, containers, security hardening)
- Time investment (ongoing maintenance, troubleshooting)

**Zen Garden lowers barrier:**
- No IP configuration (automatic discovery)
- Visual infrastructure (colored devices = easy mental model)
- Minimal maintenance (plug-and-forget Stones)

**Target users:**
- Small business owners (5-10 employees, no IT staff)
- Privacy advocates (self-host without expertise)
- Educators (hands-on learning infrastructure)
- Hobbyists (home lab experimentation)

### Digital Sovereignty

**Cloud hosting creates dependencies:**
- Vendor lock-in (proprietary APIs, migration friction)
- Surveillance capitalism (usage tracking, data mining)
- Regulatory vulnerability (GDPR, data residency laws)
- Service termination risk (account bans, price increases)

**Local ownership restores agency:**
- Data stays on-premise (full control)
- No usage tracking (zero telemetry to vendors)
- GDPR compliance simplified (data never leaves EU/jurisdiction)
- No account lock-out risk (physical possession = access)

### Global South Context

**Maintainer perspective (Brazil):**
- E-waste disproportionately affects developing nations
- Functional devices discarded in wealthy countries → landfills in Global South
- Economic barrier to cloud hosting higher (currency exchange rates)
- Local infrastructure ownership critical for digital independence

**Not a "Western solution imposed"** - this protocol originates from lived experience with e-waste impact and economic constraints of cloud dependence.

**Partnerships to explore:**
- **Brazil**: Green Eletron (national e-waste recycling)
- **Latin America**: RELAC (regional e-waste network)
- **Nigeria/Ghana**: E-waste collection programs (Accra, Lagos)
- **India**: MPCB e-waste guidelines, local refurbishment initiatives

---

## Educational Pathways

### Why Education Matters

**Infrastructure is abstract to most students:**
- "Database" = magic cloud API
- No mental model of physical servers
- Debugging by trial-and-error (no causal understanding)

**Zen Garden makes infrastructure tangible:**
- Blue device = database, green = storage, orange = compute
- Unplug blue Stone → app fails (immediate cause-effect)
- Swap Stones → app reconnects (infrastructure as Lego blocks)

### 5-Module Curriculum (Outline)

**Module 1: E-Waste Crisis (30 minutes)**
- Global e-waste statistics (UN data)
- Lifespan extension strategies
- Environmental impact of manufacturing vs. repurposing
- Activity: Calculate CO2 avoided by repurposing 1 device

**Module 2: Hardware Basics (60 minutes)**
- What's inside a computer (CPU, RAM, storage)
- When is hardware "too slow" (benchmarking)
- Power consumption measurement
- Activity: Benchmark old laptop, determine suitable service

**Module 3: Discovery Protocol (90 minutes)**
- How mDNS works (RFC 6762/6763)
- Service announcement and discovery
- Connection string resolution
- Activity: Announce service, discover from peer device

**Module 4: Deploy First Stone (90 minutes)**
- Install Debian on old laptop (NewStone.ps1 USB installer)
- Install service (MongoDB, Redis, PostgreSQL)
- Configure announcement
- Activity: Deploy Stone, connect from app

**Module 5: Measure Impact (60 minutes)**
- Calculate e-waste prevented (device lifespan extension)
- Estimate carbon avoided (manufacturing + cloud alternative)
- Document journey (before/after photos, impact narrative)
- Activity: Create impact report, share results

**Total: ~5 hours (1 intensive day or 5 weekly sessions)**

**Target audiences:**
- High school students (environmental science, computer science classes)
- Maker spaces / hackathons
- Community colleges (IT programs)
- Code bootcamps (infrastructure module)

### Potential Educational Partners

**Explored concepts (no active partnerships yet):**

**Code.org (Hour of Code)**
- 40M+ students globally
- 1-hour coding activities
- Potential: "Build Your First Database Stone" activity
- Reach: Schools in 180+ countries

**Library Programs**
- Public libraries as community tech hubs
- Micro-datacenters for local services (catalog, digital archives)
- Job training programs (IT skills via hands-on projects)
- Example: Brooklyn Public Library's Tech Hub model

**Circular Economy Organizations**
- Call2Recycle (North America battery/electronics recycling)
- e-Stewards (certified e-waste recycling network)
- Green Eletron (Brazil national e-waste system)
- RELAC (Latin America e-waste collaboration)

**Environmental Science Standards**
- NGSS (Next Generation Science Standards) alignment
- Engineering design process (problem → solution → measure)
- Systems thinking (device lifecycle, energy flows)
- Data-driven decision making (CO2 calculations, impact metrics)

---

## Carbon Impact Calculator (Concept)

**Goal:** Quantify environmental benefit of repurposing specific devices.

**Inputs:**
- Device type (laptop, desktop, thin client)
- Year manufactured (determines baseline efficiency)
- Service type (MongoDB, Redis, storage)
- Expected runtime hours/day

**Outputs:**
- CO2 avoided from delayed manufacturing (kg CO2e)
- Energy consumption vs. cloud alternative (kWh/year)
- E-waste prevented (device weight, toxic materials)
- Social share message ("I prevented 127kg CO2 by repurposing a 2015 laptop")

**Implementation:**
- Web tool (simple form → instant results)
- Embeddable widget (add to project websites)
- API (integrate into Stone dashboard)

**Data sources:**
- Manufacturing emissions: LCA studies (Lifecycle Assessment), IPCC reports
- Cloud baseline: Azure/AWS carbon intensity data
- Device efficiency: TDP (Thermal Design Power) specifications

**Social component:**
- Generate shareable impact graphic (device photo + CO2 number)
- Leaderboard (community with most repurposed devices)
- Milestones (1 tonne CO2 saved, 100 devices repurposed)

---

## Sustainability Model

**This is not a commercial project.** Open protocol maintained by Sylin.org.

**Costs covered by:**
- Maintainer investment (Sylin.org organizational resources)
- Potential future partnerships (educational grants, circular economy initiatives)
- Community contributions (documentation, code, translations)

**No revenue targets.** Success measured by:
- Devices repurposed (primary metric)
- Educational adoptions (schools, maker spaces)
- Community health (contributors, active projects)
- Protocol implementations (Rust, Python, Go, etc.)

**Future ideas (not current plans):**
- Educational content partnerships (course platform revenue share)
- Certification program (hardware testing, known-good devices)
- Circular economy collaborations (e-waste collection incentives)

**None of these are requirements for success.** Protocol succeeds if devices get repurposed and self-hosting becomes accessible.

---

## Success Metrics

**Primary:**
- Devices repurposed (self-reported via optional telemetry)
- E-waste prevented (tonnes, calculated from device count × lifespan extension)
- CO2 avoided (tonnes CO2e, calculated from manufacturing + cloud alternative)

**Secondary:**
- Educational adoptions (schools, maker spaces documenting use)
- Community retention (returning contributors month-over-month)
- Protocol implementations (libraries in different languages)

**Not success metrics:**
- GitHub stars (vanity metric)
- User count (unverifiable without telemetry)
- Media coverage (awareness ≠ impact)

**Transparency commitment:**
- Publish impact data quarterly (devices, e-waste, CO2)
- Document data sources and calculation methodology
- Acknowledge uncertainties (self-reporting bias, estimation errors)
- No greenwashing - report actual verified impact only

---

## What This Is Not

**Not a commercial product:**
- Zero revenue model
- No paid tiers or premium features
- Open protocol, multiple implementations welcome

**Not a replacement for cloud:**
- Cloud has valid use cases (global scale, compliance)
- Zen Garden optimizes for 3-30 devices, not 300

**Not zero-trust security:**
- Pond adds mTLS, not full zero-trust architecture
- Best for home labs and small businesses, not enterprises

**Not a silver bullet for e-waste:**
- Repurposing extends lifespan, doesn't prevent all waste
- Eventually devices do fail (hardware mortality)
- Complements recycling, doesn't replace it

---

## Get Involved

**Ways to contribute:**
- Repurpose a device, document the journey
- Write protocol implementation (Python, Go, Rust)
- Translate documentation (non-English regions)
- Create educational materials (lesson plans, workshops)
- Partner with e-waste organizations (circular economy)

**Current status:** Design phase. Specification and reference implementation in progress.

**Maintainer:** Sylin.org (Koan Framework maintainer)  
**License:** Apache 2.0 (permissive open source)  
**Contact:** GitHub issues/discussions (zen-garden repository)

---

## Further Reading

- [README.md](README.md) - Protocol overview
- [UNDERSTANDING.md](UNDERSTANDING.md) - Technical details
- [HARDWARE.md](HARDWARE.md) - Build physical Stones
- [ROADMAP.md](ROADMAP.md) - Implementation timeline
