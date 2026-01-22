# Failure (Weather)

*Or: what the garden does when storms arrive.*

---

## Gardens Experience Weather

Here is something obvious that systems often forget: things fail.

Containers crash. Hardware dies. Networks partition. Disks fill. Memory exhausts. Power flickers. Cables get unplugged by curious cats. The question is not whether failure will happen. The question is what the system does when it does.

Zen Garden uses weather as its failure vocabulary. Not because it's cute (though it is), but because weather is *intuitable*. You know what a storm feels like. You know that frost is different from drought. You don't need a runbook to understand that rain means "something needs attention."

This isn't metaphor for metaphor's sake. It's cognitive scaffolding. When the vocabulary carries meaning, operators develop instincts instead of memorizing procedures.

---

## The Weather Patterns

```
Condition       System State                 Operator Experience
─────────────────────────────────────────────────────────────────────
Clear           All healthy                  You barely notice
Rain            Degradation                  Something needs attention
Storm           Active failure               Alarms, intervention needed
Frost           Intentional dormancy         Quiet, waiting
Drought         Resource exhaustion          Capacity limits reached
─────────────────────────────────────────────────────────────────────
```

### Clear

Everything works. Services are healthy. Stones are reachable. Healthchecks pass. The garden is thriving.

You barely notice clear weather. That's the point. Clear is the absence of signal, the background hum of a system doing what it should. You check the garden occasionally, see green indicators, move on with your day.

Clear is the goal. Everything else is deviation from clear.

### Rain

Something isn't quite right.

A service is running hot—CPU elevated, response times increased. A healthcheck is flaky—passing sometimes, failing occasionally. A disk is filling—not full yet, but trending. A Stone is responding slowly—reachable, but laggy.

Rain is a signal, not a crisis. You might investigate. You might watch and wait. The system is functional, but degraded. Conditions could improve on their own (the traffic spike passes) or worsen into storm (the disk actually fills).

Rain invites attention without demanding it.

### Storm

Active failure. Something broke.

A container is crash-looping. A Stone went offline. A deployment failed mid-way. A certificate expired. The system is responding—rolling back, restarting, alerting—but operator attention is required.

Storms are loud. They demand action. But they also *pass*. The system is designed to weather them, not collapse under them. A failed deployment rolls back. A crashed container restarts. The garden bends but doesn't break.

After the storm: assess damage, understand cause, restore clear.

### Frost

Intentional dormancy.

You stopped a service to free resources. You powered down a Stone for maintenance. You put the garden into a quiet state while you travel. Services are not running, but that's expected. Data is preserved. Configuration is intact.

Frost is not failure. Frost is rest. The garden is waiting for warmth—for you to wake it, to restart what was stopped, to bring the Stone back online.

Frost is chosen. That's what distinguishes it from storm.

### Drought

Resource exhaustion.

Not enough memory to start another container. Not enough disk to write logs. Not enough CPU to handle the load. The garden has reached its limits and cannot do more without intervention.

Drought is different from storm. A storm is something breaking. Drought is nothing breaking, but also nothing *able* to continue. You can't wait out a drought. You must add resources (more RAM, more disk, another Stone) or reduce demand (stop services, archive data, shed load).

Drought is the garden telling you: I've given everything I have.

---

## Failure Philosophy

These principles guide how Zen Garden handles failure:

### 1. Prefer Recovery Over Alerting

If the system can fix itself, it should.

A container crashes? Restart it automatically (Docker's restart policy). A deployment fails? Roll back to the previous state. A Stone disappears? Remove it from discovery and continue.

Alerting is for failures the system *cannot* handle. If it can handle them, it should handle them quietly. The operator's attention is a scarce resource; don't spend it on problems that solve themselves.

### 2. Atomic Operations with Rollback

Operations that change state should be all-or-nothing.

When you deploy a service, Zen Garden:
1. Backs up the current Compose file
2. Writes the new configuration
3. Runs `docker compose up`
4. Waits for healthcheck
5. On success: deletes backup, announces service
6. On failure: restores backup, runs `docker compose up`, returns error

The garden is never left in a half-changed state. Either the new configuration works, or you're back where you started. This is not cleverness. This is basic operational hygiene.

### 3. Graceful Degradation

Each layer fails independently.

If Lantern goes down, local discovery still works. If mDNS fails, UDP broadcast still works. If broadcast fails, `--at` explicit targeting still works. The system has layers, and each layer can fail without destroying the layers beneath it.

The degradation is not invisible—you'll see warnings, you'll know capability is reduced. But you can still operate. The garden limps rather than collapses.

### 4. Clear Communication

When things fail, explain why.

Not "Error 503." Not "Service unavailable." Tell the operator:
- What happened
- Why it happened (if known)
- What they can do about it

```
✗ MongoDB failed to start

Reason: Port 27017 already in use
        Process 'mongod' (PID 12345) is binding this port

Suggestions:
  • Stop the existing MongoDB: sudo systemctl stop mongod
  • Use a different port: garden-rake offer mongodb --port 27018
  • Check what's using the port: sudo lsof -i :27017
```

Error messages are user interface. They deserve the same care as any other interaction.

### 5. No Unrecoverable States

There must always be a path back to working.

If a deployment breaks, you can roll back. If discovery fails, you can target explicitly. If certificates expire, you can regenerate them. If configuration corrupts, you can regenerate from templates.

The system should never paint you into a corner. Every failure state should have an escape route, even if that route is "start over from scratch."

---

## Failure Modes in Practice

Let's be specific about what can fail and what happens.

### Service Crash

**What happens:** A container exits unexpectedly.

**System response:**
1. Docker restarts container (restart policy: unless-stopped)
2. Moss detects restart, increments restart counter
3. If restart succeeds, health returns to normal
4. If restart loops (>3 in 10 minutes), Moss marks service as degraded
5. Announcement updates to `health=degraded`

**Operator experience:** If it self-heals, you see nothing (or a brief blip in monitoring). If it loops, you see degraded status and investigate.

### Stone Offline

**What happens:** A Stone stops broadcasting, stops responding to HTTP.

**System response:**
1. Other Stones notice absence (no broadcast for 90 seconds)
2. Stone removed from discovery cache
3. Services on that Stone become unreachable
4. `zen-garden:mongodb` resolution fails if that was the only MongoDB

**Operator experience:** Services on the dead Stone are unavailable. Other Stones continue operating. You investigate the offline Stone.

### Deployment Failure

**What happens:** `garden-rake offer mongodb` fails mid-deployment.

**System response:**
1. Image pull fails? Report error, no changes made
2. Container start fails? Stop container, restore previous Compose, report error
3. Healthcheck fails? Stop container, restore previous Compose, report error

**Operator experience:** The command returns an error explaining what failed. The garden is in the same state as before you ran the command. You can investigate and retry.

### Network Partition

**What happens:** Some Stones can reach each other, others can't.

**System response:**
1. Each partition operates independently
2. Stones see only the Stones they can reach
3. Discovery returns only reachable services
4. No "split-brain" because there's no shared state to disagree about

**Operator experience:** Different parts of the garden see different things. When partition heals, broadcasts resume, everyone converges. The stateless design means there's nothing to reconcile.

### Disk Full

**What happens:** No space left on device.

**System response:**
1. Container writes fail, logs may truncate
2. Docker may stop accepting new operations
3. Moss continues running (it's mostly in-memory)
4. Health endpoint reports degraded with disk warning

**Operator experience:** You see drought conditions. Services are running but can't write. You must free space or add storage.

---

## Recovery Procedures

When storms pass, recovery follows patterns.

### Restarting a Failed Service

```bash
# Check what's wrong
garden-rake observe --at stone-01

# Look at logs
garden-rake watch mongodb logs --at stone-01

# Restart the service
garden-rake wake mongodb --at stone-01

# Or remove and re-plant
garden-rake take-away mongodb --at stone-01
garden-rake offer mongodb --at stone-01
```

### Recovering from Stone Failure

```bash
# If Stone comes back online, it announces itself
# Other Stones will see it within 90 seconds

# If Stone is permanently dead, its services are gone
# Re-plant them elsewhere
garden-rake offer mongodb --at stone-02
```

### Recovering from Corrupt Configuration

```bash
# Moss regenerates Compose from templates
garden-rake reconcile --at stone-01

# Or start fresh
garden-rake take-away --all --at stone-01
garden-rake offer mongodb redis postgresql --at stone-01
```

### Recovering from Expired Certificates

```bash
# Regenerate certificates (requires Cornerstone access)
garden-rake pond refresh

# Or remove Pond entirely and start over
garden-rake take-away keystone
garden-rake place keystone
garden-rake invite stone-02 stone-03
```

---

## What Weather Communicates

The weather vocabulary does more than name failure states. It communicates *severity* and *agency*.

```
Weather     Severity    Agency              What You Do
─────────────────────────────────────────────────────────────────────
Clear       None        N/A                 Nothing (enjoy it)
Rain        Low         System or operator  Watch, maybe investigate
Storm       High        System + operator   Intervene, understand, fix
Frost       None        Operator chose it   Wake when ready
Drought     High        Operator must act   Add resources or shed load
─────────────────────────────────────────────────────────────────────
```

Weather language lets operators communicate quickly:

- "We've got rain on stone-02" — something's degraded, not urgent
- "Storm in progress, rolling back" — active failure, system responding
- "Drought on the cache tier" — out of resources, need to expand
- "Database is in frost" — intentionally stopped, don't panic

This is not jargon. It's shared vocabulary that carries meaning.

---

## What Comes Next

Weather describes failure. But why does the garden exist in the first place?

The final pillar, **Joy**, explains what we're actually trying to achieve. Not just "working infrastructure"—delightful infrastructure. Systems that make people smile instead of sigh. The humanist mission made operational.

---

*Zen Garden Documentation — Technical Pillars*
