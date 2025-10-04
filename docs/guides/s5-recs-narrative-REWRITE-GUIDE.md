# S5.Recs Narrative Rewrite Guide

## Voice & Tone: "The Contemplative Architect's Walk"

### Principles Applied

1. **Eliminate AI text pitfalls**
   - Remove em dashes (—) → Use periods or commas
   - Remove "Let's explore..." / "It's worth noting..." → Direct statements
   - Remove rhetorical questions that answer themselves
   - Reduce bullet point overuse → Integrate into prose
   - Remove triple emphasis (bold + italics + caps)

2. **Consistent voice**
   - Confident architect giving thoughtful tour
   - Patient without condescension
   - Trust reader intelligence (eliminate hand-holding)

3. **Sentence structure**
   - Shorter, punchier sentences
   - Active voice dominates
   - Natural transitions without signposting

4. **Technical depth**
   - Maintained but woven into narrative
   - Definitions integrated, not footnoted
   - Trust reader to ask or research

## Transformation Examples

### BEFORE (Act/Scene theatrical structure):
```markdown
## Act I: The Foundation (Entity-First Storage)

### Scene 1: Just Store Media

Every journey starts simple. You have media content from AniList...
```

### AFTER (Direct section headers):
```markdown
## Foundation: Entity-First Storage

### Storing Media Content

Media content arrives from AniList's API. The first requirement: store it.
```

---

### BEFORE (Em dashes, verbose):
```markdown
Notice something? **`media.Save()`** is a one-liner. No repositories. No DbContext. No dependency injection setup.

**Why this approach instead of Entity Framework's repository pattern?**
```

### AFTER (Direct, integrated):
```markdown
Look at that `media.Save()` call. One line. Behind it, the framework handles persistence, provider selection, and error handling. No repositories, no DbContext, no dependency injection setup.

Compare this to traditional Entity Framework patterns:
```

---

### BEFORE (Bullet lists with bold headers):
```markdown
**Why this approach wins:**
1. **Zero manual work**: No synonym curation
2. **Contextual understanding**: "bank" near "money"...
3. **Multilingual**: Handles romanized Japanese...
```

### AFTER (Integrated prose):
```markdown
This approach delivers five advantages: zero manual work (no synonym curation or corpus preprocessing), contextual understanding ("bank" near "money" versus "river" produces different embeddings), multilingual handling...
```

---

### BEFORE (Exclamation points, "Great!", casual asides):
```markdown
You just added semantic search. Great! But then users report:

> "I search for..."

**The Problem:**
```

### AFTER (Calmer, more measured):
```markdown
Semantic search works. Then users report a problem:

> "I search for..."

The embedding model (all-MiniLM-L6-v2) trained primarily on English text.
```

---

### BEFORE (Over-explaining with footnotes):
```markdown
**Exponential Moving Average (EMA) Explained:**

*A technique from signal processing and time-series analysis that gives more weight to recent data while retaining historical context.*

```
Traditional average (equal weight):
  Rating 1: "K-On!" → [0.8, 0.2, 0.1]
  ...

Why "exponential"?
  Each new rating has diminishing influence...
```

### AFTER (Integrated definition, trust reader):
```markdown
The solution uses exponential moving average (EMA) from signal processing. Recent ratings influence the vector more than older ones, but history persists:

```
Rating 1: "K-On!" → PrefVector = [0.8, 0.2, 0.1]
Rating 2: "Death Note" → PrefVector = 70% old + 30% new
  = [0.65, 0.32, 0.22]
```

---

### BEFORE (Rhetorical question patterns):
```markdown
**The Challenge:**

How do you combine them?

**Option 1: Only Use Search Intent**

**Problem:** Doesn't personalize...
```

### AFTER (Direct problem statement):
```markdown
A logged-in user searches for "magic school anime". Two signals exist: search intent (what they want now) and learned preferences (what they generally like). Combining them requires balance.

**Only Use Search Intent**

Doesn't personalize. Alice and Bob get identical results...
```

---

### BEFORE (Academic footnotes breaking flow):
```markdown
Scalability: Comparing users is O(N²) *(means if you double users, computation time quadruples)*
```

### AFTER (Integrated or omitted if non-essential):
```markdown
Scalability: Comparing users is O(N²). Double the users, quadruple the computation.
```
(Or just omit the explanation if the audience should know Big-O notation)

---

## Section-Specific Changes

### Introduction
- Remove target audience bullets
- Replace "Imagine you're building" with "Picture..."
- Remove "This isn't a tutorial—it's a narrative"
- Trust document to show its own nature

### Technical Comparisons
- Keep Option 1/2/3 pattern (educational value)
- Shorten each option description
- Remove "**Better!**" and "**Seems balanced!**" exclamations
- Replace with measured assessment

### Code Examples
- Keep code intact (high value)
- Reduce surrounding explanation
- Comments in code should be terse
- Let code speak more, prose less

### Architecture Sections
- Remove "For Architects:" callout boxes
- Integrate architectural insights naturally
- Trust reader to extract relevant level

### Trade-offs
- Keep trade-off discussions (important)
- Format: "Trade-off: X sacrifices Y. In return, you gain Z."
- No bold "Trade-off Accepted:" headers

## Voice Consistency Checklist

- [ ] No em dashes (—)
- [ ] No "Let's..." or "It's important to note..."
- [ ] No rhetorical questions with immediate answers
- [ ] No exclamation points (except in quotes or rare emphasis)
- [ ] Sentences average <20 words
- [ ] Active voice >80% of time
- [ ] No inline footnotes with asterisks
- [ ] Headers are direct, not theatrical
- [ ] Bullet lists converted to prose where possible
- [ ] Technical terms defined in flow, not callout boxes
- [ ] Consistent second-person "you" OR third-person, not mixed

## Tone Temperature

**Current doc oscillates:**
- Hot: "Great!" "Notice something?" "**Better!**"
- Cold: Dense technical explanations with academic rigor

**Target tone:**
- Measured: Confident but not excited
- Patient: Explains without over-explaining
- Appreciative: Shows craft without gushing
- Direct: States facts without hedging

**Analogy:**
Not a tour guide pointing excitedly ("Look at this amazing feature!")
Not a professor lecturing ("It is important to understand that...")
But an architect walking through their building, explaining choices with quiet confidence.

## Implementation Status

**Completed sections:**
- Introduction (lines 1-50)
- Foundation: Entity-First Storage (lines 51-280)
- Adding Intelligence: Semantic Search (lines 281-450)
- Start of Hybrid Search (lines 451-480)

**Remaining sections:**
- Hybrid Search completion
- Personalization (Act IV)
- Performance/Caching (Act V)
- UX Modes (Act VI)
- Feature Synergy (Act VII)
- Technical Deep Dive (Act VIII)
- Lessons/Patterns (Act IX)
- Conclusion

**Estimated effort:** ~3-4 hours for full document rewrite
**Pattern established:** Can be replicated throughout

## Next Steps

1. Continue systematic rewrite through remaining 1400 lines
2. Final pass for voice consistency
3. Remove any remaining AI text patterns
4. Verify code examples unchanged (only prose edited)
5. Check all headers follow new pattern
6. Ensure technical accuracy preserved
