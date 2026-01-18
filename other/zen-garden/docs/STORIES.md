# Community Stories

**Real-world use cases from different perspectives.**

---

## Story 1: Small Business Owner

**Context:**
- 8-person marketing agency
- No dedicated IT staff
- Growing cloud costs ($200-300/month)
- Privacy concerns (client data in cloud)

### Before Zen Garden

**Infrastructure:**
- Dropbox Business: $15/user/month = $120/month
- MongoDB Atlas: $57/month (M10 cluster)
- Heroku web hosting: $75/month
- Total: $252/month = $3,024/year

**Pain points:**
- Costs increasing with each new employee
- GDPR compliance anxiety (client data offshore)
- Vendor lock-in (migration friction)
- No visibility into where data actually lives

### After Zen Garden

**Setup:**
- Repurposed 2017 laptop → MongoDB Stone (client database)
- Old desktop → MinIO Stone (file storage, 2TB drives)
- Thin client ($50 eBay) → Redis Stone (session cache)

**Investment:**
- Hardware: $50 (thin client only, others already owned)
- Time: 4 hours setup (weekend project)
- Electricity: $40/year (three devices @ 15-20W each)

**Total cost: $90 first year, $40/year ongoing**  
**Savings: $2,934/year (97% reduction)**

### Impact

**Technical:**
- Apps connect via `zen-garden:mongodb`, `zen-garden:storage`
- No config changes when router reboots (automatic discovery)
- Physical infrastructure visible (blue Stone = database, green = files)

**Business:**
- GDPR compliance simplified (data never leaves office)
- Client trust increased (local data = privacy selling point)
- Cost savings redirect to marketing budget

**Environmental:**
- 3 devices repurposed (extended lifespan 3+ years)
- ~900kg CO2 avoided (manufacturing + cloud alternative)
- E-waste prevented: ~6kg devices + toxic components

### Quote

> "I can point to the device that has our client data. That conversation changed how clients see us—we're the privacy-conscious agency now."

---

## Story 2: Privacy Advocate

**Context:**
- Software developer
- Strong privacy principles (no cloud when avoidable)
- Runs self-hosted services (Nextcloud, Bitwarden, Jellyfin)
- Home lab with 5-8 devices

### Before Zen Garden

**Infrastructure:**
- Docker Compose files with hardcoded IPs
- Manual updates when DHCP reassigns addresses
- SSH into each device to check service status
- Remote debugging via VPN tunnels

**Pain points:**
- Brittle configuration (IP changes break apps)
- Maintenance overhead (3-4 hours/month)
- No visual feedback (services invisible, remote-only)
- Family can't troubleshoot ("just restart everything")

### After Zen Garden

**Setup:**
- MongoDB Stone (personal app data)
- PostgreSQL Stone (Nextcloud database)
- MinIO Stone (media storage, Jellyfin backend)
- Redis Stone (session cache)
- Ollama Stone (local LLM, privacy-preserving AI)

**Configuration:**
```bash
# .env file (never changes)
DATABASE_URL=zen-garden:mongodb
CACHE_URL=zen-garden:redis
STORAGE_URL=zen-garden:minio
LLM_URL=zen-garden:ollama
```

**Benefits:**
- Zero IP management (automatic discovery)
- Physical debugging (unplug Stone → app fails → causal clarity)
- Family-friendly (color-coded Stones, visual troubleshooting)

### Impact

**Technical:**
- Maintenance reduced to ~30 minutes/month (OS updates only)
- Uptime improved (services survive DHCP changes)
- Experimentation easier (swap Stones, apps reconnect automatically)

**Personal:**
- Digital sovereignty achieved (zero cloud dependencies)
- Family onboarded (spouse uses Nextcloud, understands infrastructure)
- Knowledge sharing (blog post attracted 50+ other privacy advocates)

**Environmental:**
- 5 devices repurposed (2014-2018 laptops/thin clients)
- Cloud alternative offset: ~250kg CO2/year
- E-waste prevented: ~10kg devices

### Quote

> "I finally understand my own infrastructure. Each Stone has a purpose, and I can explain it by pointing at physical devices. My spouse gets it now too."

---

## Story 3: Educator (Computer Science)

**Context:**
- High school computer science teacher
- 25 students per class
- Limited budget ($500/year for supplies)
- Teaching infrastructure concepts (databases, caching, storage)

### Before Zen Garden

**Approach:**
- Cloud accounts (MongoDB Atlas free tier, AWS educate credits)
- Abstract diagrams (whiteboard architecture)
- Individual student projects (no shared infrastructure)

**Limitations:**
- Students don't see physical hardware
- Debugging is trial-and-error (no causal understanding)
- Cloud credits run out (projects break mid-semester)
- No environmental context (sustainability not in curriculum)

### After Zen Garden

**Setup:**
- School IT donated 4 decommissioned laptops (2015-2017)
- Converted to Stones: MongoDB (blue tape), Redis (orange), MinIO (green), Compute (yellow)
- Classroom demonstration: Visible infrastructure on table

**Curriculum integration:**

**Week 1: E-Waste Crisis**
- UN statistics (62M tonnes/year)
- Lifespan extension strategies
- Environmental impact of manufacturing vs. repurposing
- Activity: Calculate CO2 avoided by repurposing 1 device

**Week 2: Hardware Basics**
- What's inside a computer (open laptop, show components)
- Benchmark old laptop (sufficient for database)
- Power measurement (15W laptop vs. cloud alternative)
- Activity: Choose service type for device capabilities

**Week 3: Discovery Protocol**
- mDNS explanation (RFC 6762/6763)
- Service announcement demo (watch Lantern dashboard)
- Connection string resolution (zen-garden:mongodb → actual URI)
- Activity: Announce service, discover from peer device

**Week 4: Deploy App**
- Students write app using `zen-garden:mongodb`
- Teacher unplugs blue Stone → app fails (causal understanding)
- Plug in different MongoDB Stone → app reconnects automatically
- Activity: Handle connection failures gracefully (retry logic)

**Week 5: Measure Impact**
- Calculate e-waste prevented (4 devices × 3-year extension)
- Estimate CO2 avoided (manufacturing + cloud alternative)
- Document journey (before/after photos, impact narrative)
- Activity: Present findings to class, social media share

### Impact

**Educational:**
- Students understand infrastructure physically (not just abstractly)
- Debugging improves (proximity = causal clarity)
- Environmental awareness integrated (sustainability in CS curriculum)
- 25 students exposed to e-waste reduction concept

**School:**
- Zero ongoing cost (donated hardware, existing network)
- Curriculum alignment (NGSS standards, environmental science)
- Showcase project (open house demonstration for parents)

**Environmental:**
- 4 devices repurposed (would have been recycled prematurely)
- Student projects use local Stones (not cloud credits)
- Ripple effect: Students discuss at home, potentially influence families

### Quote

> "When I unplug the blue Stone and the app crashes, they finally *get* it. That moment of 'oh, the database is a physical thing' changes how they think about infrastructure."

---

## Story 4: Developer (Freelancer)

**Context:**
- Full-stack freelancer
- 5-10 client projects simultaneously
- Rapid prototyping (test ideas, demo MVPs)
- Development/staging environments

### Before Zen Garden

**Infrastructure:**
- Local Docker Compose (per-project stacks)
- Port conflicts (multiple PostgreSQL instances)
- Manual coordination (which DB is which project?)
- Cloud staging ($50-100/month for demo environments)

**Pain points:**
- Cognitive overhead (remember port mappings)
- Context switching (stop Project A stack, start Project B)
- Staging costs (client demos require live URLs)
- Cleanup neglect (old containers accumulate)

### After Zen Garden

**Setup:**
- 3 old laptops repurposed:
  - Stone 1: MongoDB + PostgreSQL (multi-tenant databases)
  - Stone 2: Redis (shared cache)
  - Stone 3: Compute Stone (Docker host for apps)

**Workflow:**
```bash
# Project A
export DATABASE_URL=zen-garden:mongodb/projecta
garden-rake push projecta-api --image projecta:latest

# Project B
export DATABASE_URL=zen-garden:postgresql/projectb
garden-rake push projectb-api --image projectb:latest

# No port conflicts, automatic discovery
```

**Benefits:**
- Zero port management (services on default ports, mDNS disambiguates)
- Shared infrastructure (one MongoDB Stone, multiple databases)
- Physical feedback (Stone status LEDs = health monitoring)
- Client demos (local URLs like `http://projecta.garden/`)

### Impact

**Technical:**
- Setup time reduced (1 hour per project → 10 minutes)
- Debugging faster (physical Stones = visual troubleshooting)
- Prototyping velocity increased (no infrastructure friction)

**Business:**
- Staging costs eliminated ($600-1,200/year savings)
- Client confidence higher (tangible infrastructure during demos)
- Differentiation (sustainability angle in proposals)

**Environmental:**
- 3 devices repurposed (personal upgrades, still functional)
- Cloud staging offset: ~100kg CO2/year
- E-waste prevented: ~6kg devices

### Quote

> "I spend time building features, not managing port conflicts. The Stones just sit there and work. When clients visit, I show them the physical devices running their app—they love it."

---

## Story 5: Maker Space Community

**Context:**
- Community workshop (tools, electronics, 3D printers)
- 150 members, 20-30 active weekly
- Teaching goal: Hands-on tech skills
- Budget: Minimal (donations, volunteer-run)

### Before Zen Garden

**Challenges:**
- No shared infrastructure (members bring laptops, work independently)
- WiFi-only (devices not discoverable, no collaboration)
- E-waste accumulation (donated laptops too slow for desktop use)
- No environmental program (sustainability mission unclear)

### After Zen Garden

**Project: "Maker Cloud" - Community Infrastructure**

**Setup:**
- 8 donated laptops (2013-2017, "too slow" for donors)
- Converted to Stones:
  - 2× MongoDB Stones (member project databases)
  - 1× PostgreSQL Stone (makerspace inventory system)
  - 2× MinIO Stones (shared file storage, 3D print files)
  - 1× Redis Stone (session cache)
  - 2× Compute Stones (Docker hosts for member apps)

**Physical installation:**
- Visible shelf (labeled Stones, color-coded)
- LED status indicators (power, network, disk activity)
- Documentation poster (which Stone offers which service)

**Member benefits:**
```bash
# Any member's project can use shared infrastructure
export DATABASE_URL=zen-garden:mongodb/myproject
export STORAGE_URL=zen-garden:minio

# Automatic discovery on maker space WiFi
# No account creation, no cloud sign-up
```

### Impact

**Community:**
- 30+ member projects using Stones (IoT sensors, web apps, data logging)
- Collaboration increased (shared infrastructure = shared context)
- Workshops added (Zen Garden setup as curriculum module)

**Educational:**
- E-waste awareness (donated laptops → productive infrastructure)
- Physical computing emphasized (see infrastructure, touch Stones)
- Sustainability narrative (makerspace mission now includes e-waste reduction)

**Environmental:**
- 8 devices repurposed (would have been recycled or landfilled)
- Community model scalable (other makerspaces replicating)
- Ripple effect: Members repurpose devices at home, tell friends

**Financial:**
- Zero ongoing cost (donated hardware, existing network)
- Cloud alternative avoided ($100-200/month for shared infrastructure)

### Quote

> "We took laptops that donors thought were useless and turned them into the backbone of our community projects. Members see them on the shelf and realize—old tech isn't trash."

---

## Common Themes

### Technical

**Before:**
- Hardcoded IPs, brittle configuration
- Manual coordination, maintenance overhead
- Remote debugging, opacity

**After:**
- Automatic discovery, zero IP management
- Physical infrastructure, visual troubleshooting
- Reduced maintenance, increased reliability

### Environmental

**Devices repurposed:**
- 2-8 devices per story (personal to community scale)
- Lifespan extension: 3-5 years average
- E-waste prevented: 4-16kg per story

**CO2 avoided:**
- Manufacturing offset: 600-2,400kg (2-8 devices × 300kg each)
- Cloud alternative: 50-500kg/year (depends on usage)
- Total: 750-4,900kg CO2e over 5 years

### Economic

**Savings:**
- Small business: $2,934/year (cloud replacement)
- Privacy advocate: $600/year (maintenance time value)
- Educator: $500/year (cloud credits avoided)
- Freelancer: $1,000/year (staging environments)
- Maker space: $1,200/year (shared infrastructure)

**Investment:**
- Hardware: $0-200 (repurposed or used thin clients)
- Time: 2-8 hours initial setup
- Ongoing: Minimal (OS updates, electricity)

---

## Your Story

**Repurposed a device? Documented your journey?**

Share your story:
- GitHub discussions (zen-garden repository)
- Blog post with photos (before/after, impact calculation)
- Social media (#ZenGarden, #EWasteReduction)

**We showcase community stories** (with permission) to inspire others and measure collective impact.

---

## Further Reading

- [Mission](MISSION.md) - Environmental and social goals
- [Hardware Guide](HARDWARE.md) - Build your first Stone
- [Getting Started](GETTING-STARTED.md) - Quick setup (5 minutes)
- [Understanding](UNDERSTANDING.md) - How protocol works
