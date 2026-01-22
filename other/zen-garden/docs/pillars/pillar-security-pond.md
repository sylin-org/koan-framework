# Security (Pond)

*Or: what happens when you add water to the garden.*

---

## Fill When Ready

Here is the security philosophy in one sentence: *set your stones, make sure everything is working, fill the pond.*

Security comes last. Not because it's unimportant—because it's important enough to get right. And you cannot get security right on a system you don't yet understand.

Too many projects demand security configuration before anything works. Generate certificates. Configure authentication. Set up key rotation. Meanwhile, you don't know if the basic functionality even runs in your environment. You're debugging TLS handshakes when you should be debugging whether the service starts at all.

Zen Garden inverts this. Start without security. Get discovery working. Deploy a service. Confirm that your Stones see each other, that your applications can connect, that the basic machinery turns. *Then* fill the pond.

This isn't security nihilism. It's sequencing. Security on a broken system is theater. Security on a working system is protection.

---

## What Water Does

A pond changes a garden.

Before the pond, everything is visible. You can walk anywhere. There are no barriers between one part of the garden and another. Anyone can enter, look around, touch the stones.

After the pond, there's a boundary. Some stones sit above water, some below. The submerged stones are still visible—but through water, differently. To reach them, you must enter the pond. The pond has an edge, an inside and an outside.

In Zen Garden, the **Pond** is a security layer built on mutual TLS. When you fill a pond:

- Stones receive certificates identifying them
- Communication between Stones is encrypted
- New Stones must be *admitted*—they can't just wander in
- The boundary becomes real: inside the pond, you're trusted; outside, you're not

The metaphor maps precisely to the technical reality. Water is encryption. The pond's edge is the trust boundary. Submersion is membership.

---

## The Admission Ceremony

How does a new Stone join a pond? Through something familiar: pairing.

You've done this with Bluetooth devices. Put one device in pairing mode. It shows a code. Type the code on the other device. Now they trust each other.

Pond admission works the same way:

```
On the Cornerstone (existing Stone with authority):
$ garden-rake invite stone-02

Invitation ready for: stone-02

TOTP Code: KP7X9M
Valid for: 5 minutes

Display this code to the administrator at stone-02.
```

```
On stone-02 (the new Stone):
$ garden-rake pond join --code KP7X9M

Validating code...
✓ Code accepted
✓ Certificate issued
✓ Joined pond "garden-pond"

stone-02 is now part of the pond.
```

That's it. No pre-shared secrets. No certificate signing requests. No PKI ceremony. Just: show a code, type a code, you're in.

The code is a TOTP—a time-based one-time password, the same technology your two-factor authentication app uses. It's valid for five minutes. It can only be used once. And critically, it requires *physical proximity*: someone must read the code from one screen and type it on another. You cannot join a pond from across the internet by guessing.

This is security that feels like pairing your headphones. Familiar. Obvious. Hard to get wrong.

---

## The Cornerstone

Every pond has a **Cornerstone**: the first Stone, the one that holds authority.

When you fill a pond, the Stone you're on becomes the Cornerstone. It generates a certificate authority—a keypair that can issue certificates to other Stones. This keypair is the **Keystone**: the cryptographic foundation that everything else rests on.

```
$ garden-rake place keystone

Initializing Pond...

How would you like to create your passphrase?
1. Let me mash the keyboard! (fun & secure)
2. Generate one for me (quick & easy)
3. I'll type my own (advanced)

Choice [1]: 1

Mash your keyboard randomly... GO!
████████████████████ 100%

✓ Collected 248 bits of entropy

Generated passphrase: forest-lantern-compass-71

✓ Pond created
✓ Cornerstone: stone-01
✓ Keystone sealed

Next: garden-rake invite <stone-name>
```

The passphrase protects the Keystone. If someone steals the file, they still need the passphrase to use it. The keyboard mashing isn't a gimmick—it's entropy collection. Your random key-presses generate randomness that seeds the passphrase generator.

If your hardware has a TPM (Trusted Platform Module), the Keystone is sealed in hardware automatically. The system detects what protection is available and uses the strongest option. You don't have to configure this. It just happens.

---

## Two Depths

Not all ponds are the same depth.

**Garden Pond (Tier 1)** is for home labs and small teams. It assumes:

- Single administrator, or small group of trusted people
- Physical security (the Stones are in your house, your office, your rack)
- Threats are accidents more than attacks

Garden Pond provides:

- Encryption in transit (no one can sniff your traffic)
- Authentication (Stones prove their identity)
- Admission control (new Stones must be explicitly invited)
- Short-lived certificates (1 hour, auto-renewed)

This is enough for most home labs. It stops casual snooping and prevents random devices from joining your garden. It doesn't stop a determined attacker with physical access to your Cornerstone.

**Deep Pond (Tier 2)** is for environments with real adversaries:

- Multiple administrators who don't fully trust each other
- Compliance requirements (GDPR, SOC2, HIPAA)
- Threat model includes insider attacks

Deep Pond adds:

- Hardware security (TPM required, not optional)
- Multi-signature operations (sensitive actions need multiple approvals)
- Distributed audit logs (tamper-evident record of everything)
- Advanced certificate management

Deep Pond is not yet implemented. It's designed, but cultivation takes time. Tier 1 is solid. Tier 2 is coming.

The choice between depths is a choice about your threat model. What are you protecting against? Accidents, or attacks? Outsiders, or insiders? The answer determines which pond you need.

---

## What Pond Protects

Let's be specific.

**Authentication.** Without Pond, any device that can reach your network can announce "I am a Stone offering MongoDB." With Pond, only devices holding valid certificates are trusted. Rogue devices are ignored.

**Encryption.** Without Pond, traffic between Stones is plaintext. Anyone on the network can see it. With Pond, all inter-Stone communication is TLS-encrypted. Sniffing sees noise.

**Admission.** Without Pond, Stones discover each other automatically. Plug in a new device, it joins the garden. With Pond, new Stones must be explicitly invited. The garden has a boundary.

**Tamper detection.** Certificates are bound to Stone identity. If something claims to be stone-01 but presents the wrong certificate, the connection fails. You cannot impersonate a Stone without its private key.

---

## What Pond Does Not Protect

Equally specific.

**Physical access.** If an attacker can touch the Cornerstone, they can extract the Keystone file. The passphrase slows them down; it doesn't stop them. Deep Pond with TPM helps—the key is hardware-bound—but physical security remains your responsibility.

**Compromised Stones.** If malware runs on a Stone, it has the Stone's private key. It can do anything that Stone can do. Pond authenticates Stones, not the software running on them.

**Time attacks.** TOTP codes depend on synchronized clocks. If an attacker controls your NTP server, they might replay codes. Garden Pond has a wide tolerance (±10 minutes). Deep Pond adds clock consensus.

**Network availability.** Pond doesn't protect against someone unplugging your network. Denial of service is out of scope.

**Nation-states.** If your threat model includes APT (Advanced Persistent Threat) actors, Zen Garden is not your tool. Deep Pond is serious security, but it's not "defend against intelligence agencies" security.

Knowing what you're *not* protected against is as important as knowing what you are. Security theater—pretending protection you don't have—is worse than no protection at all.

---

## The Visibility Change

Here's something subtle that the metaphor captures: when you fill a pond, *visibility changes*.

Without Pond, you see all Stones equally. Discovery returns everything. There's no concept of "inside" or "outside."

With Pond, Stones inside the pond are visible differently. Discovery still works, but trust is evaluated. Announcements from non-pond Stones are ignored (or flagged, depending on configuration). The pond creates a filter.

This isn't just security—it's *perception*. The pond changes what you see because it changes what counts as real. An announcement from an untrusted source isn't hidden; it's *discounted*. You can still notice it (that's useful for debugging), but the system doesn't act on it.

Water changes light. Depth changes clarity. The metaphor holds.

---

## Filling Your Pond

If you've decided you need a pond, here's the path:

**1. Verify everything works without security first.**
Run discovery. Deploy a service. Confirm your Stones see each other. Don't debug security and functionality simultaneously.

**2. Choose your Cornerstone.**
This is the Stone that will hold the Keystone—the certificate authority. Pick something stable. Not the Raspberry Pi that overheats. Not the laptop you might take traveling.

**3. Place the keystone.**
```
$ garden-rake place keystone
```
Follow the prompts. Protect your passphrase. The system will use TPM if available.

**4. Invite your other Stones.**
```
$ garden-rake invite stone-02
```
Walk to stone-02 (or SSH in), run the join command with the code. Repeat for each Stone.

**5. Verify pond status.**
```
$ garden-rake pond status

Pond Status: Active (Garden Pond)
Cornerstone: stone-01
Stones: 4 joined
Certificates: All valid, renewing normally
```

**6. Update your applications.**
If your applications connect via `zen-garden:` connection strings, they'll automatically use the secure path. If they use direct IPs, you may need to update them to go through Moss.

That's it. Pond filled. Garden secured.

---

## When Not to Fill

The pond is optional. Here's when to leave it empty:

- **Solo home lab, trusted network.** If it's just you, in your house, on your network, with no sensitive data—the overhead of Pond may not be worth it. The garden works fine without water.

- **Experimentation and learning.** When you're just trying things out, security adds friction. Learn how the system works first. Add the pond when you have something worth protecting.

- **Air-gapped or physically secured environments.** If no untrusted device can reach the network, the threat model that Pond addresses doesn't apply.

Security is not a moral obligation. It's a response to threats. If the threats don't apply, the response is unnecessary.

---

## What Comes Next

The pond protects the garden. But what grows in the garden?

The next pillar, **Offerings**, explains how services are deployed—not through ad-hoc commands, but through curated templates that encode best practices. The garden offers what the garden has been tended to offer.

---

*Zen Garden Documentation — Technical Pillars*
