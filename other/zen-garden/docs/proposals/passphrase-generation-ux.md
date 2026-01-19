# Proposal: Playful Passphrase Generation UX

**Author**: Workshop Panel (DX, UX, Security, Semiotics)  
**Date**: 2026-01-18  
**Status**: Proposed

## Problem Statement

Keystone security depends on strong passphrases, but users struggle with:
- **Randomness anxiety**: "I don't know what's random enough"
- **Memorability**: Complex passphrases are hard to remember
- **Cognitive load**: "How long? Which characters? Is this secure?"

Traditional password requirements (`Tr0ub4dor&3`) are both weak (28 bits entropy) and hard to remember. XKCD 936 demonstrated that four random words (`correct-horse-battery-staple`, 44 bits) is both stronger and more memorable.

**Goal:** Make passphrase generation delightful, educational, and secure.

## Proposal

Offer **three passphrase generation methods**, with keyboard mashing as the fun default:

### Method 1: Entropy Collection with Optional Participation (Default - Recommended)
Turn entropy collection into a discoverable, rewarding experience.

**UX Flow (Passive User):**
```bash
$ garden-rake place keystone

How would you like to create your passphrase?

1. Generate with entropy collection (recommended)
2. I'll type my own (advanced)

Choice [1]: 1

Generating secure passphrase...
(Tip: Type anything to speed this up!)

█░░░░░░░░░░░░░░░░░░░ 10%
████░░░░░░░░░░░░░░░░ 20%
████████░░░░░░░░░░░░ 40%
████████████░░░░░░░░ 60%
████████████████░░░░ 80%
████████████████████ 100% - Done!

✓ Collected 256 bits of entropy from system
  
Generated from 9.3 seconds

Your passphrase: emerald-bicycle-coffee-91
```

**UX Flow (Active User):**
```bash
Generating secure passphrase...
(Tip: Type anything to speed this up!)

█░░░░░░░░░░░░░░░░░░░ 10%

[User discovers they can type]
[Types a few characters]

███████░░░░░░░░░░░░░ 35% (nice! keep going to speed this up)

[User types more enthusiastically]
████████████░░░░░░░░ 60% (you're making this fly!)
████████████████████ 100% - Done!

✓ Collected 287 bits of entropy
  • 220 bits from system (urandom + timing)
  • 67 bits from your keyboard (42 keypresses)
  
Generated from 4.2 seconds (you saved 5.8 seconds by typing!)

Your passphrase: forest-lantern-compass-71
```

**Why this works:**
- **Zero anxiety**: Works perfectly without any user input (just wait)
- **Emergent discovery**: Users naturally discover typing speeds it up
- **Optional engagement**: User chooses their level of participation
- **Genuine entropy**: System entropy (urandom) + optional keystroke timing
- **Immediate reward**: Each keypress visibly speeds up completion
- **Accessibility**: Works for users with mobility issues (passive mode)
- **Gamification without pressure**: Fast typers save time, slow typers still succeed

**Implementation (Golden Standard):**
```rust
fn collect_entropy_with_optional_typing() -> Vec<u8> {
    use std::time::{Instant, Duration};
    use std::io::{stdin, Read};
    use rand::Rng;
    
    let mut entropy_pool = Vec::new();
    let mut rng = rand::thread_rng();
    
    // Random base duration: 8-10 seconds (works without user input)
    let base_duration_ms = rng.gen_range(8000.0..10000.0);
    let mut remaining_ms = base_duration_ms;
    
    let start = Instant::now();
    let mut last_sample = start;
    let mut keystroke_count = 0;
    
    println!("Generating secure passphrase...");
    println!("(Tip: Type anything to speed this up!)");
    
    // Set stdin to non-blocking
    let stdin = stdin();
    let mut handle = stdin.lock();
    
    loop {
        let now = Instant::now();
        let elapsed = (now - start).as_millis() as f64;
        
        // ALWAYS sample urandom every 250ms (security baseline)
        if (now - last_sample) >= Duration::from_millis(250) {
            let mut urandom_bytes = [0u8; 32];
            getrandom::getrandom(&mut urandom_bytes)?;
            entropy_pool.extend_from_slice(&urandom_bytes);
            last_sample = now;
        }
        
        // Check for keyboard input (non-blocking, optional)
        let mut buffer = [0u8; 1];
        if let Ok(_) = handle.read(&mut buffer) {
            keystroke_count += 1;
            
            // Add keystroke entropy (bonus!)
            let timing = now.elapsed().as_nanos() as u64;
            entropy_pool.push(buffer[0]);
            entropy_pool.extend_from_slice(&timing.to_le_bytes());
            
            // REWARD: Each keypress saves 50-90ms
            let time_saved = rng.gen_range(50.0..90.0);
            remaining_ms -= time_saved;
            
            // Encouragement based on progress
            let progress = ((base_duration_ms - remaining_ms) / base_duration_ms * 100.0).min(100.0) as usize;
            let encouragement = match progress {
                0..=30 => "(nice! keep going to speed this up)",
                31..=70 => "(you're making this fly!)",
                71..=99 => "(almost there!)",
                _ => "",
            };
            
            print!("\r{} {}% {}", "█".repeat(progress / 5), progress, encouragement);
            std::io::stdout().flush()?;
        }
        
        // Calculate actual progress (time-based OR keystroke-accelerated)
        let target_elapsed = base_duration_ms - remaining_ms;
        if elapsed >= target_elapsed {
            println!("\r████████████████████ 100% - Done!");
            break;
        }
        
        // Update progress bar (even without typing)
        let progress = (elapsed / target_elapsed * 100.0).min(100.0) as usize;
        print!("\r{} {}%", "█".repeat(progress / 5), progress);
        std::io::stdout().flush()?;
        
        std::thread::sleep(Duration::from_millis(50));
    }
    
    let final_duration = (Instant::now() - start).as_secs_f64();
    let total_bits = entropy_pool.len() * 8;
    let keyboard_bits = keystroke_count * 8; // Rough estimate
    let systemManual Entry (Advanced)
For users who have their own passphrase or use password managers:
    Sha256::digest(&entropy_pool).to_vec()
}

fn generate_xkcd_passphrase(entropy: &[u8]) -> String {
    // EFF large wordlist (7776 words, ~12.9 bits per word)
    let words = include_str!("eff_large_wordlist.txt").lines();
    
    // Use entropy to select 4 words + number
    let mut indices = [0u16; 4];
    for (i, chunk) in entropy.chunks(2).take(4).enumerate() {
        indices[i] = u16::from_le_bytes([chunk[0], chunk[1]]) % 7776;
    }
    
    let number = entropy[8] % 100;
    
    format!("{}-{}-{}-{}", 
        words[indices[0]], words[indices[1]], 
        words[indices[2]], number)
}
```

### Method 2: Auto-Generated (Quick)
For users who want "just generate something secure":

**UX Flow:**
```bash
$ garden-rake place keystone --generate-passphrase

Generated passphrase: compass-twilight-harvest-82

Entropy: 52 bits (strong)
Memorization tip: "A compass points to twilight during harvest #82"

Want a different one? [Enter] to regenerate, [Y] to use this: y
```

**Features:**
- Instant generation (no interaction needed)
- Regenerate with single keypress
- Same XKCD-style output (4 words + number)

### Method 3: Manual Entry (Advanced)
For users who have their own passphrase or use password managers:

**UX Flow:**
```bash
$ garden-rake place keystone

How would you like to create your passphrase?

1. Let me mash the keyboard! (fun & secure)
2. Generate one for me (quick & easy)
3. I'll type my own (advanced)

Choice [1]: 3

Enter passphrase: ************
Strength: ████████░░ Strong (48 bits)
Instant Generation (Power Users)
Skip entropy collection entirely
```

**Live strength indicator:**
```bash
Enter passphrase: pass
Strength: ██░░░░░░░░ Very weak (12 bits)

EntGenerate with entropy collection (recommended)
2. I'll type my own (advanced)

Choice [1]: 2██████░ Strong (44 bits)

Enter passphrase: emerald-bicycle-coffee-meadow-91
Strength: ██████████ Excellent (60 bits)
```

**Rejection with education:**
```bash
Enter passphrase: password123
✗ Too weak (entropy: 18 bits, need 40+)

Why? This passphrase would take ~0.3 seconds to crack 
with modern hardware. Good passphrases use 4+ random words.

Examples:
  • emerald-bicycle-coffee-91
  • purple-mountain-lighthouse-42
  • compass-twilight-harvest-82

Generate a secure passphrase? [Y/n]:
```

## XKCD-Style Passphrases

Following XKCD 936 philosophy: https://xkcd.com/936/

**Format:** `word1-word2-word3-number`

**Example:** `forest-lantern-compass-71`

**Entropy calculation:**
- 4 words from EFF list (7776 words): 4 × 12.9 bits = 51.6 bits
- 2-digit number (0-99): 6.6 bits
- **Total: ~58 bits** (more than sufficient for AES-256)

**Why hyphens?**
- Visual parsing: Clearly four concepts + number
- Uncommon in passwords: Adds slight entropy
- No shift key: Easy to type
- Linguistic clarity: Signals "sequence of items"

**Memorization aids:**
```
forest-lantern-compass-71
→ "A forest with lanterns, using compass #71 to navigate"

purple-mountain-lighthouse-42  
→ "Purple mountain with lighthouse, tower #42"
```

Users naturally create mini-stories from random words.

## Advanced: Diceware Method

For maximum security (truly random, air-gapped):

**UX Flow:**
```bash
$ garden-rake place keystone --diceware

Diceware Passphrase Generation
===============================
This method uses physical dice for true randomness.
Your passphrase cannot be predicted or reproduced by attackers.

You'll need: 5 standard dice (6-sided)

Roll all 5 dice, enter the numbers (e.g., 43216):

Roll 1: 43216
Word 1: "emerald" ✓

Roll 2: 15634
Word 2: "bicycle" ✓

Roll 3: 66421
Word 3: "coffee" ✓

Roll 4: 23456
Word 4: "meadow" ✓

Your passphrase: emerald-bicycle-coffee-meadow

This was generated using physical randomness (dice rolls),
providing maximum security. Save it securely!

Continue? [Y/n]:
```

**When to use:**
- Extreme threat model (nation-state adversaries)
- Compromised machine (offline passphrase generation)
- Regulatory compliance (FIPS 140-2 requires hardware RNG)

## Passphrase Validation

**Minimum requirements:**
- Entropy: 40 bits (acceptable)
- Strong: 52 bits (4 XKCD words)
- Excellent: 60+ bits

**Using `zxcvbn` crate:**
```rust
fn validate_passphrase(passphrase: &str) -> Result<PassphraseScore> {
    let entropy = zxcvbn::zxcvbn(passphrase, &[])?;
    
    let score = PassphraseScore {
        bits: entropy.guesses_log10() * 3.32, // log10 to bits
        rating: match entropy.score() {
            0..=1 => "Very weak",
            2 => "Weak",
            3 => "Acceptable",
            4 => "Strong",
            5.. => "Excellent",
        },
        stars: "⭐".repeat(entropy.score()),
        crack_time: entropy.crack_times_display().offline_slow_hashing_1e4_per_second,
    };
    
    if entropy.score() < 3 {
        return Err(anyhow!(
            "Passphrase too weak ({}). Suggestions:\n  - {}",
            score.rating,
            entropy.feedback().suggestions().join("\n  - ")
        ));
    }
    
    Ok(score)
}
```

## File-Based Passphrases (Automation)

For headless setups, support non-interactive modes:

```bash
# From file
garden-rake place keystone --passphrase-file /secure/passphrase.txt

# From environment
ZEN_KEYSTONE_PASS="forest-lantern-compass-71" garden-rake place keystone

# From stdin (for CI/CD)
echo "forest-lantern-compass-71" | garden-rake place keystone --passphrase-stdin
```

**Security warnings:**
```
⚠ Passphrase loaded from file: /secure/passphrase.txt
  Ensure file permissions are 0600 (read/write owner only)
  Consider deleting after setup or moving to encrypted storage
```

## Educational Messaging

**On weak passphrase:**
```
✗ Too weak (18 bits, need 40+)

This passphrase would take ~0.3 seconds to crack with 
modern GPU cluster. Strong passphrases use randomness.

Good patterns:
  ✓ 4+ random words: "forest-lantern-compass-71"
  ✓ Uncommon words: Better than common words
  ✓ Add numbers: Increases entropy slightly

Try again or generate? [G/retry]:
```

**On strong passphrase:**
```
✓ Excellent passphrase (60 bits) ⭐⭐⭐⭐⭐

Estimated crack time: 36 million years (offline attack)
Your keystone is well-protected!
```

**Links to education:**
```
Learn more about passphrase security:
  • XKCD comic: https://xkcd.com/936/
  • EFF Dice-Generated Passphrases: https://eff.org/dice
  • Our guide: https://zen-garden.dev/docs/security/passphrases
```

## Success Metrics

**Adoption:**
- 60%+ users choose keyboard mashing (Method 1)
- 30% use auto-generated (Method 2)
- 10% manual entry (Method 3)

**Quality:**
- 95%+ of passphrases exceed 40 bits entropy
- <5% of users submit weak passphrases more than once

**Delight:**
- Users report passphrase generation as "fun" in surveys
- NPS score >8 for security setup experience

## Implementation Checklist

**Phase 1: XKCD Generator (MVP)**
- [ ] Integrate EFF large wordlist (7776 words)
- [ ] Implement 4-word + number generator
- [ ] Add entropy validation with `zxcvbn`
- [ ] Show strength indicator during manual entry
- [ ] Educational rejection messages

**Phase 2: Keyboard Mashing (Delight)**
- [ ] Collect keystroke timing entropy
- [ ] Progress bar with visual feedback
- [ ] Hash entropy pool with SHA-256
- [ ] Generate XKCD passphrase from entropy
- [ ] Memorization tips

**Phase 3: Advanced Features**
- [ ] Diceware mode (5-dice rolls)
- [ ] Regenerate option (single keypress)
- [ ] Passphrase strength meter (live feedback)
- [ ] File/environment variable support

**Phase 4: Polish**
- [ ] Profanity filter for generated words
- [ ] Customizable word count (3-6 words)
- [ ] Localization (wordlists in multiple languages)
- [ ] Export to password managers (1Password, Bitwarden)

## References

- XKCD 936: https://xkcd.com/936/ (Password Strength)
- EFF Wordlists: https://www.eff.org/dice
- Diceware: https://diceware.dmuth.org/
- `zxcvbn` entropy estimation: https://github.com/dropbox/zxcvbn

## Related Decisions

- SECURITY-0003: Keystone Protection Tiers (what we're protecting)
- SECURITY-0002: Keystone Rename (naming clarity)

## Workshop Insights

**Dr. Okonkwo (UX):** "Keyboard mashing turns security into a playful interaction. Users participate in their own protection."

**Aria (Vocab Ergonomics):** "The word 'mash' carries joy. It's the sound of a toddler hitting piano keys. It signals permission to be chaotic."

**Prof. Chen (Semantics):** "Four random words create a mini-story. Humans are good at remembering narratives."

**Marina (Semiotics):** "Hyphens signal 'these are separate equal items in a sequence' - semantically clearer than concatenation."

**Dr. Tanaka (Security):** "Keystroke timing is genuinely unpredictable. This isn't security theater, it's real entropy."

## Conclusion

Make passphrase generation the most delightful part of Zen Garden setup. Turn security from a burden into an engaging experience that teaches users why strong passphrases matter.

**Philosophy:** Security should feel empowering, not restrictive.
