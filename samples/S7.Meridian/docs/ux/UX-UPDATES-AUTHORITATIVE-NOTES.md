# UX Documentation Updates: Authoritative Notes Feature

**Date**: October 22, 2025
**Purpose**: Document all UX updates made to incorporate the Authoritative Notes Override feature

---

## Summary

Updated all UX documentation files to reflect the Authoritative Notes feature that allows users to provide free-text data that unconditionally overrides AI document extractions. This feature was specified in detail in `AUTHORITATIVE-NOTES-PROPOSAL.md` and now fully integrated into the UX proposal.

---

## Files Updated

### 1. `00-EXECUTIVE-SUMMARY.md`

**Changes**:
- Added "authoritative notes override" to Vision Statement
- Added **Authoritative Notes** bullet point under "Trust Through Transparency" principle:
  > "User-provided data always overrides AI extractions (marked with ⭐ gold star)"

**Impact**: Executive summary now highlights this differentiating feature that provides user control over AI extractions.

---

### 2. `02-PAGE-LAYOUTS.md`

**Changes**:

#### Added: Pipeline Setup Screen Components
```
- **Authoritative Notes field** (800px width, 160px height)
  - Gold background: rgba(251, 191, 36, 0.08)
  - Gold border-left: 4px solid #FBBF24
  - Icon: ⭐ 24×24px, gold
  - Placeholder: "Optional: Enter data that should override document extractions..."
  - Helper text: "Any information you enter here will take priority over extracted values"
```

#### Enhanced: Review & Resolve Screen Components
```
- Field tree (left pane, scrollable)
  - **Gold star indicator (⭐)** for fields sourced from Authoritative Notes
  - Confidence indicators for all other fields
- Evidence drawer (480px slide-in from right)
  - Shows "Authoritative Notes (User Override)" as source when applicable
  - "Edit Notes" button available for Notes-sourced fields
```

**Impact**: Clear visual specifications for gold-themed Notes UI elements across two critical screens.

---

### 3. `01-USER-JOURNEYS.md`

**Changes**:

#### Phase 1: Pipeline Setup Journey (Enhanced)
Added visual mockup of Authoritative Notes field in Pipeline Setup screen:

```
│ ⭐ Authoritative Notes (Optional):                         │
│ ┌───────────────────────────────────────────────┐          │
│ │ CEO Name: Dana Martinez                      │          │
│ │ Employee Count: 475 (as of Sept 2024 call)  │ ← TYPES │
│ └───────────────────────────────────────────────┘          │
│ [Gold background: rgba(251, 191, 36, 0.08)]                │
│ [Gold border-left: 4px solid #FBBF24]                      │
│                                                             │
│ ℹ️ Any data entered here will override extractions.       │
│    Use free-text format - AI will interpret fields.        │
```

#### Phase 4: Review & Resolve Journey (Enhanced)
1. **Updated conflict count**: Changed from "⚠ 3 Conflicts" to "⚠ 2 Conflicts" because CEO Name is now provided via Notes (no conflict)

2. **Added gold star indicator** in field tree:
   ```
   │ │ ▾ Contact Info │ Company Overview              │        │
   │ │   • CEO Name ⭐│ Founded: 2018                 │        │
   │ │   • Email ⚠   │ Employees: 450 full-time      │        │
   ```

3. **Added Evidence Drawer example** for Notes-sourced field:
   ```
   │ [User clicks on "CEO Name ⭐" to see evidence]            │
   │                                                             │
   │ EVIDENCE DRAWER slides in from right:                      │
   │ ┌──────────────────────────────────────────────────┐       │
   │ │ Evidence for "CEO Name"                     [×] │       │
   │ ├──────────────────────────────────────────────────┤       │
   │ │ Selected Value: Dana Martinez                   │       │
   │ │ ┌────────────────────────────────────────────┐   │       │
   │ │ │ ⭐ Source: Authoritative Notes (User Override)│  │       │
   │ │ │ ┌──────────────────────────────────────┐   │   │       │
   │ │ │ │ "CEO Name: Dana Martinez"           │   │   │       │
   │ │ │ │  Employee Count: 475 (as of Sept...) │  │   │       │
   │ │ │ └──────────────────────────────────────┘   │   │       │
   │ │ │ [Edit Notes] [View All Notes]             │   │       │
   │ │ │ Confidence: 100% (User-provided)          │   │       │
   │ │ │ Entered: During pipeline setup            │   │       │
   │ │ │ [Gold background: rgba(251, 191, 36, 0.08)]│  │       │
   │ │ └────────────────────────────────────────────┘   │       │
   │ │                                                  │       │
   │ │ ℹ️ This value was provided in Authoritative    │       │
   │ │    Notes and overrides any document extractions │       │
   │ └──────────────────────────────────────────────────┘       │
   ```

**Impact**: Demonstrates complete user workflow from entering Notes during setup to seeing Notes-sourced values during review, including visual treatment.

---

### 4. `03-COMPONENT-LIBRARY.md`

**Changes**: Added three new component specifications

#### Component 1: Authoritative Notes Field (Pipeline Setup)
- **Purpose**: Free-text input for user-provided data that overrides AI extractions
- **Dimensions**: 800px × 160px
- **Visual Theme**: Gold (#FBBF24)
  - Background: `rgba(251, 191, 36, 0.08)`
  - Border-left: `4px solid #FBBF24`
  - Icon: ⭐ 20×20px
- **States**: Default (empty), Focus, Filled, Hover
- **Accessibility**: Full HTML example with ARIA attributes

#### Component 2: Gold Star Indicator (Review Screen)
- **Purpose**: Mark fields sourced from Authoritative Notes with visual distinction
- **Size**: 20×20px (in field tree)
- **Color**: #FBBF24 (Yellow 400, gold)
- **Animation**: Subtle pulse on first render (2 iterations)
- **States**: Default, Hover (with tooltip), Pulse animation
- **Tooltip**: "Sourced from Authoritative Notes"

#### Component 3: Evidence Drawer - Authoritative Notes Source Card Variant
- **Purpose**: Show Notes as source with gold-themed card
- **Visual Theme**: Matches Notes field (gold background/border)
- **Content**:
  - Source label: "⭐ SOURCE: AUTHORITATIVE NOTES (USER OVERRIDE)"
  - Gold-themed passage block
  - Action buttons: [Edit Notes] [View All Notes] [View Full Context]
  - Metadata: "Confidence: 100% (User-provided)", "Entered: During pipeline setup"
  - Info message explaining override behavior

#### Component 4: Modal Dialog Variant - Edit Authoritative Notes Modal
- **Icon**: ⭐ 32×32px, gold
- **Header**: Gold bar (4px height, `rgba(251, 191, 36, 0.08)`)
- **Textarea**: Gold border on focus
- **Three-button layout**:
  1. Cancel (secondary, left)
  2. Save without re-processing (tertiary, middle)
  3. Save & re-process (primary, right, gold theme)
- **Warning message**: "⚠️ Re-processing will take ~2-3 minutes and may update extracted values"

**Impact**: Complete component specifications ready for implementation, covering all Authoritative Notes UI elements.

---

## Visual Design Language: Gold Theme

### Color Palette for Authoritative Notes
```css
/* Primary Gold */
--gold-500: #FBBF24 (Yellow 400) - Borders, icons
--gold-600: #F59E0B (Amber 500) - Accents, hover states
--gold-700: #D97706 (Amber 600) - Text labels

/* Backgrounds */
--gold-bg-light: rgba(251, 191, 36, 0.08) - Default backgrounds
--gold-bg-medium: rgba(251, 191, 36, 0.12) - Hover backgrounds
--gold-bg-highlight: rgba(251, 191, 36, 0.35) - Highlighted text

/* Borders */
--gold-border: 4px solid #FBBF24 - Left border accent
--gold-border-focus: 2px solid #FBBF24 - Focus states
```

### Icon Usage
- **Star emoji**: ⭐ (U+2B50) - Used consistently across all Notes-related UI
- **Size variants**:
  - 20×20px: Field tree indicators, labels
  - 24×24px: Pipeline setup field icon
  - 32×32px: Modal headers

---

## User Experience Principles

### 1. Silent Override (No Notifications)
- Notes-sourced fields show **gold star (⭐)** but no warning/conflict badge
- User is not notified that Notes override occurred - it "just works"
- Transparency through Evidence Drawer: Click star → See "Authoritative Notes" as source

### 2. Progressive Disclosure
- **Tier 1**: Gold star visible in field tree (passive indicator)
- **Tier 2**: Click star → Evidence Drawer shows Notes content
- **Tier 3**: Click "Edit Notes" → Modal with re-processing options

### 3. Visual Consistency
- Gold theme (#FBBF24) used exclusively for Authoritative Notes
- Same background/border treatment across:
  - Pipeline Setup textarea
  - Evidence Drawer source card
  - Edit Notes modal
- Star icon (⭐) as universal symbol for Notes-sourced data

### 4. Free-Text Flexibility
- No structured input required (no forms, dropdowns, or field-specific inputs)
- Placeholder text shows example format: "CEO Name: Jane Smith, Employee Count: 500"
- Helper text emphasizes: "Use free-text format - AI will interpret field names"
- Users can use natural language, abbreviations, or any format

---

## Interaction Patterns

### Pattern 1: Entering Authoritative Notes (Pipeline Setup)
```
1. User navigates to Pipeline Setup
2. Sees gold-themed "⭐ Authoritative Notes (Optional)" field
3. Enters free-text data (e.g., "CEO Name: Dana Martinez")
4. Continues to upload documents
5. Processing extracts from both Notes and documents
6. Notes values automatically take precedence
```

### Pattern 2: Reviewing Notes-Sourced Fields
```
1. User lands on Review screen
2. Sees field tree with some fields marked "⭐"
3. Hovers over star → Tooltip: "Sourced from Authoritative Notes"
4. Clicks field or star → Evidence Drawer opens
5. Sees gold-themed source card: "Authoritative Notes (User Override)"
6. Clicks "View All Notes" → See complete Notes text
```

### Pattern 3: Editing Notes After Processing
```
1. User clicks "Edit Notes" in Evidence Drawer
2. Modal opens with current Notes content in textarea
3. User edits text, clicks "Save & re-process"
4. Warning appears: "Re-processing will take ~2-3 minutes..."
5. User confirms
6. Pipeline re-processes, Notes values still override
7. Field values update, gold stars remain on Notes-sourced fields
```

---

## Accessibility Considerations

### ARIA Labels
```html
<!-- Gold Star Indicator -->
<span
  class="gold-star"
  aria-label="Sourced from Authoritative Notes"
  role="img">
  ⭐
</span>

<!-- Notes Field -->
<textarea
  id="auth-notes"
  aria-describedby="notes-helper"
  aria-label="Authoritative Notes: Enter data to override AI extractions">
</textarea>
```

### Screen Reader Announcements
- Field tree navigation: "CEO Name, star, sourced from Authoritative Notes"
- Evidence Drawer: "Evidence for CEO Name. Source: Authoritative Notes, User Override"
- Edit modal: "Edit Authoritative Notes. Warning: Re-processing will update values."

### Keyboard Navigation
- **Tab** to Notes field in Pipeline Setup
- **Tab** through field tree, **Enter** on starred fields to open Evidence Drawer
- **Tab** through Evidence Drawer buttons: "Edit Notes", "View All Notes", "Close"
- **Escape** to close Evidence Drawer or modal

---

## Implementation Priority

### Phase 1: Core Components (Week 1-2)
1. Authoritative Notes Field (Pipeline Setup)
   - Gold-themed textarea with icon
   - Helper text and placeholder
   - Character count (optional)

2. Gold Star Indicator (Review Screen)
   - Icon in field tree
   - Pulse animation on first load
   - Tooltip on hover

### Phase 2: Evidence Display (Week 2-3)
3. Evidence Drawer Variant
   - Gold-themed source card for Notes
   - "Edit Notes" button
   - Information message

### Phase 3: Editing Workflow (Week 3-4)
4. Edit Authoritative Notes Modal
   - Three-button layout
   - Warning message for re-processing
   - Integration with backend re-processing API

---

## Success Metrics

### User Adoption
- **Target**: 40% of pipelines use Authoritative Notes (within 3 months)
- **Measure**: Track `authoritativeNotes` field usage in Pipeline entity

### User Satisfaction
- **Target**: >85% of users find Notes feature "very useful" (NPS survey)
- **Measure**: Post-pipeline survey: "How useful was Authoritative Notes?"

### Override Accuracy
- **Target**: <5% of users re-edit Notes after seeing results
- **Measure**: Track "Edit Notes" button clicks in Evidence Drawer

### Time Savings
- **Target**: Notes reduces manual conflict resolution time by 30%
- **Measure**: Compare conflict count for pipelines with/without Notes

---

## Open Questions for Stakeholder Review

1. **Character Limit**: Should we enforce a character limit on Notes field? (Recommendation: 2000 chars, ~300 words)

2. **Template Support**: Should we offer Notes templates for common analysis types?
   - Example: "Enterprise Architecture Review" → Pre-populated with "CEO Name: [Enter], Employee Count: [Enter]"

3. **Version History**: Should we track Notes edit history for audit trails?
   - Current: No version history (matches stakeholder decision #4: no notifications)
   - Alternative: Store edit timestamps for compliance scenarios

4. **Bulk Import**: Should we allow CSV import into Notes field?
   - Use case: User has spreadsheet with 50 vendor names → Copy-paste into Notes
   - Risk: Very long Notes content may slow AI extraction

5. **Notes Preview**: Should Pipeline Setup show a preview of how Notes will be interpreted?
   - Example: "We detected 2 field overrides: CEO Name, Employee Count"
   - Risk: Adds complexity to simple free-text pattern

---

## Conclusion

All UX documentation has been updated to fully incorporate the Authoritative Notes Override feature. The design maintains consistency with Meridian's core principles:

✅ **Trust Through Transparency**: Gold star indicators and Evidence Drawer show exactly what was overridden
✅ **Progressive Disclosure**: Notes field is optional, advanced editing is one click away
✅ **Error Prevention**: Free-text format with AI interpretation avoids rigid schemas
✅ **Narrative-First**: Notes integrate seamlessly into final deliverable citations

**Next Steps**:
1. Stakeholder review and approval of UX updates
2. Create Figma mockups based on specifications
3. Begin frontend implementation (Phase 1: Core Components)
4. User testing with 5 moderated sessions using Notes workflow

---

**Document Author**: Senior UX/UI Design Team
**Review Status**: Ready for stakeholder approval
**Related Documents**:
- `AUTHORITATIVE-NOTES-PROPOSAL.md` (Technical specification)
- `00-EXECUTIVE-SUMMARY.md` (Updated)
- `01-USER-JOURNEYS.md` (Updated)
- `02-PAGE-LAYOUTS.md` (Updated)
- `03-COMPONENT-LIBRARY.md` (Updated)
