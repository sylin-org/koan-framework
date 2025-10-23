# Meridian UX Proposal: Information Architecture

**Document Type**: UX Strategy  
**Author**: Senior UX/UI Design Team  
**Date**: October 22, 2025  
**Version**: 1.0  
**Target Audience**: Product, Engineering, Design Teams

---

## Executive Summary

This information architecture defines the structural foundation for Meridian's user experience, optimized for the primary user story: **Enterprise Architecture Review creation from multiple vendor documents**. The architecture emphasizes progressive disclosure, evidence-based trust, and a linear narrative flow that transforms complex document analysis into a guided, confidence-building journey.

---

## Design Principles Alignment

### Koan Framework Ethos
1. **Simplicity Over Complexity** - Hide technical details, show outcomes
2. **Semantic Meaning** - Every UI element communicates its purpose clearly
3. **Sane Defaults** - 90% of users never touch advanced settings
4. **Context-Aware** - System adapts to user's current task and document types

### Meridian-Specific Principles
1. **Trust Through Transparency** - Every value shows its evidence
2. **Progressive Disclosure** - Show 20% that solves 80% of problems
3. **Error Prevention** - Guardrails, not gates
4. **Narrative Flow** - Linear progression with clear next steps

---

## Site Map & Navigation Hierarchy

```
┌─────────────────────────────────────────────────────────────────┐
│                     MERIDIAN HOME                               │
│                                                                 │
│  ┌────────────────┐  ┌────────────────┐  ┌────────────────┐    │
│  │   Projects     │  │   Templates    │  │   Settings     │    │
│  │   (Primary)    │  │   (Secondary)  │  │   (Tertiary)   │    │
│  └────────────────┘  └────────────────┘  └────────────────┘    │
└─────────────────────────────────────────────────────────────────┘
         │                     │                     │
         │                     │                     │
    ┌────▼──────┐         ┌───▼────┐           ┌────▼──────┐
    │ Projects  │         │Analysis│           │ User      │
    │ Dashboard │         │Types   │           │Preferences│
    └────┬──────┘         └───┬────┘           └───────────┘
         │                    │
         │                    │
    ┌────▼────────────────┐   │
    │ CREATE NEW PROJECT  │   │
    │ (Primary Flow)      │   │
    └────┬────────────────┘   │
         │                    │
    ┌────▼─────────────────┐  │
    │ 1. CHOOSE ANALYSIS   │◄─┘
    │    TYPE              │
    └────┬─────────────────┘
         │
    ┌────▼─────────────────┐
    │ 2. NAME & CONFIGURE  │
    │    PIPELINE          │
    └────┬─────────────────┘
         │
    ┌────▼─────────────────┐
    │ 3. UPLOAD DOCUMENTS  │
    │    (Drag & Drop)     │
    └────┬─────────────────┘
         │
    ┌────▼─────────────────┐
    │ 4. REVIEW            │
    │    CLASSIFICATION    │
    └────┬─────────────────┘
         │
    ┌────▼─────────────────┐
    │ 5. PROCESS           │
    │    (Auto-run)        │
    └────┬─────────────────┘
         │
    ┌────▼─────────────────┐
    │ 6. REVIEW FIELDS     │
    │    (Split View)      │
    └────┬─────────────────┘
         │
    ┌────▼─────────────────┐
    │ 7. RESOLVE CONFLICTS │
    │    (If any)          │
    └────┬─────────────────┘
         │
    ┌────▼─────────────────┐
    │ 8. PREVIEW &         │
    │    FINALIZE          │
    └────┬─────────────────┘
         │
    ┌────▼─────────────────┐
    │ 9. DOWNLOAD /        │
    │    SHARE             │
    └──────────────────────┘
```

---

## Primary User Flow: Enterprise Architecture Review

### Flow Overview

**Scenario Context** (from ScenarioA):
- **User**: Enterprise Architect or Vendor Assessment Team Lead
- **Goal**: Create a comprehensive architecture readiness review
- **Inputs**: 4 documents (meeting notes, customer bulletin, vendor prescreen, cybersecurity assessment)
- **Outputs**: Markdown report with Key Findings, Financial Health, Staffing, Security Posture

**Time to Completion**: 8-12 minutes (vs. 4-6 hours manual)

---

### Step-by-Step Flow with Access Depth

#### **Tier 1: Primary Path** (Always Visible, Main Story)

```
┌──────────────────────────────────────────────────────────────────┐
│                                                                  │
│  Home → Create → Choose Type → Upload → Process → Review → Done │
│                                                                  │
│  ✓ Linear progression                                           │
│  ✓ Clear next action at every step                              │
│  ✓ Progress indicator always visible                            │
│  ✓ Can save and return anytime                                  │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

**Access Depth**: 0 clicks (Main Navigation)

---

#### **STEP 1: Home Dashboard**

**Purpose**: Quick access to projects and templates

**Layout**:
```
┌────────────────────────────────────────────────────────────────┐
│ MERIDIAN                    [🔍 Search]  [👤 Profile] [⚙️ Help] │
├────────────────────────────────────────────────────────────────┤
│                                                                │
│  Your Projects                                    [+ New]      │
│  ┌──────────────────┐  ┌──────────────────┐                   │
│  │ Enterprise Arch  │  │ Vendor Due Dil.  │  [View All →]    │
│  │ Review           │  │ Q3 2024          │                   │
│  │ ⏱ In Progress    │  │ ✓ Complete       │                   │
│  │ 3 of 4 docs      │  │ 12 docs          │                   │
│  └──────────────────┘  └──────────────────┘                   │
│                                                                │
│  Quick Start Templates                                         │
│  ┌──────────────────┐  ┌──────────────────┐  ┌──────────────┐ │
│  │ 📊 Vendor        │  │ 🏢 Enterprise    │  │ 📋 RFP       │ │
│  │ Assessment       │  │ Arch Review      │  │ Response     │ │
│  │ Use Template     │  │ Use Template     │  │ Use Template │ │
│  └──────────────────┘  └──────────────────┘  └──────────────┘ │
│                                                                │
│  Recent Activity                                               │
│  • "Q3 Vendor Assessment" finalized 2 hours ago               │
│  • "Security Audit 2024" processing 1 day ago                 │
│                                                                │
└────────────────────────────────────────────────────────────────┘
```

**Key Elements**:
- **Hero Action**: Large "New Project" button (primary CTA)
- **Project Cards**: Show status, progress, thumbnail preview
- **Template Cards**: Pre-configured for common scenarios
- **Recent Activity**: Build context, enable quick returns

**Interaction**:
- Click "Enterprise Arch Review" template → Step 2
- Click existing project → Resume at last step

---

#### **STEP 2: Choose Analysis Type**

**Purpose**: Select or customize the type of analysis/deliverable

**Layout**:
```
┌────────────────────────────────────────────────────────────────┐
│ ← Back to Home        Choose Analysis Type          [Skip →]   │
├────────────────────────────────────────────────────────────────┤
│                                                                │
│  What type of deliverable do you want to create?              │
│                                                                │
│  ┌────────────────────────────────────────────────────────┐   │
│  │ ◉ Enterprise Architecture Readiness Review             │   │
│  │   📊 Synthesizes architecture meeting notes, technical │   │
│  │      bulletins, vendor capabilities, security posture  │   │
│  │                                                        │   │
│  │   Output Fields: Key Findings, Financial Health,       │   │
│  │                  Staffing, Security Posture            │   │
│  │                                                        │   │
│  │   Recommended for: CIO steering committee              │   │
│  └────────────────────────────────────────────────────────┘   │
│                                                                │
│  ○ Vendor Due Diligence Report                                │
│    Financial analysis, compliance verification...             │
│                                                                │
│  ○ RFP Response Document                                      │
│    Aggregate past projects, certifications...                 │
│                                                                │
│  ○ Custom Analysis                                            │
│    Define your own fields and template                        │
│                                                                │
│                              [Continue with Selected Type →]  │
│                                                                │
└────────────────────────────────────────────────────────────────┘
```

**Key Elements**:
- **Radio Selection**: Single choice, clear visual hierarchy
- **Type Description**: Explain what it does, not technical jargon
- **Output Preview**: Show what fields will be extracted
- **Smart Default**: Pre-select most common (Enterprise Arch) based on template click

**Access Depth**: 1 click from Home

---

#### **STEP 3: Name & Configure Pipeline**

**Purpose**: Personalize the project and set optional parameters

**Layout**:
```
┌────────────────────────────────────────────────────────────────┐
│ ← Back              Configure Your Pipeline       Step 2 of 7  │
├────────────────────────────────────────────────────────────────┤
│                                                                │
│  Project Name *                                                │
│  ┌────────────────────────────────────────────────────────┐   │
│  │ Enterprise Architecture Review - Synapse Analytics     │   │
│  └────────────────────────────────────────────────────────┘   │
│                                                                │
│  Description (Optional)                                        │
│  ┌────────────────────────────────────────────────────────┐   │
│  │ Q4 2024 vendor evaluation for Atlas modernization      │   │
│  └────────────────────────────────────────────────────────┘   │
│                                                                │
│  ▼ Advanced Options (Optional)                                │
│                                                                │
│  Special Instructions                                          │
│  ┌────────────────────────────────────────────────────────┐   │
│  │ Emphasize Q3 2024 data. Prioritize security findings. │   │
│  └────────────────────────────────────────────────────────┘   │
│                                                                │
│                                     [Save Draft] [Continue →] │
│                                                                │
└────────────────────────────────────────────────────────────────┘
```

**Key Elements**:
- **Auto-Generated Name**: Date + Analysis Type (user can edit)
- **Collapsed Advanced**: Don't overwhelm new users
- **Save Draft**: Allow interruption without loss
- **Field Validation**: Inline feedback on required fields

**Access Depth**: 2 clicks from Home

---

#### **STEP 4: Upload Documents**

**Purpose**: Add source documents with live AI classification

**Layout**:
```
┌────────────────────────────────────────────────────────────────┐
│ ← Back              Upload Source Documents       Step 3 of 7  │
├────────────────────────────────────────────────────────────────┤
│                                                                │
│  ┌────────────────────────────────────────────────────────┐   │
│  │                                                        │   │
│  │              📄 Drag files here or click to browse     │   │
│  │                                                        │   │
│  │  Supports: PDF, DOCX, TXT, Images (JPG, PNG)          │   │
│  │  Max size: 50 MB per file                             │   │
│  │                                                        │   │
│  └────────────────────────────────────────────────────────┘   │
│                                                                │
│  Uploaded Files (4)                                            │
│                                                                │
│  ┌────────────────────────────────────────────────────────┐   │
│  │ 📄 meeting-notes.txt              2.1 KB     [✓] [×]  │   │
│  │ 🏷️ Meeting Notes (94% confidence)                      │   │
│  │ AI detected: Architecture discussions, decisions       │   │
│  └────────────────────────────────────────────────────────┘   │
│                                                                │
│  ┌────────────────────────────────────────────────────────┐   │
│  │ 📄 customer-bulletin.txt          1.8 KB     [✓] [×]  │   │
│  │ 🏷️ Customer Technical Bulletin (89% confidence)        │   │
│  │ AI detected: Product updates, integration highlights   │   │
│  └────────────────────────────────────────────────────────┘   │
│                                                                │
│  ┌────────────────────────────────────────────────────────┐   │
│  │ 📄 vendor-prescreen.txt           1.5 KB     [✓] [×]  │   │
│  │ 🏷️ Vendor Prescreen Questionnaire (96% confidence)    │   │
│  │ AI detected: Revenue, staffing, certifications         │   │
│  └────────────────────────────────────────────────────────┘   │
│                                                                │
│  ┌────────────────────────────────────────────────────────┐   │
│  │ 📄 cybersecurity-assessment.txt   2.3 KB     [✓] [×]  │   │
│  │ 🏷️ Cybersecurity Assessment (91% confidence)          │   │
│  │ AI detected: Controls, findings, remediation           │   │
│  └────────────────────────────────────────────────────────┘   │
│                                                                │
│  ⚠️ 1 document needs review (confidence < 70%)                │
│                                                                │
│                            [Review Classification] [Process →]│
│                                                                │
└────────────────────────────────────────────────────────────────┘
```

**Key Elements**:
- **Drag Zone**: Large, obvious target (mobile shows file picker)
- **Live Classification**: As files upload, AI classifies in real-time
- **Confidence Badges**: Green (>90%), Yellow (70-89%), Red (<70%)
- **AI Hints**: Show what the AI detected (build trust)
- **Checkmarks**: Visual progress indication
- **Warning**: Alert if any docs need manual review

**Interaction**:
- **Auto-advance**: If all docs have high confidence, "Process" button pulses
- **Manual Review**: Click warning to see/override classification
- **Remove**: X button to delete file

**Access Depth**: 3 clicks from Home

---

#### **STEP 4b: Review Classification** (Conditional)

**Purpose**: Verify or correct AI document classification

**Layout**:
```
┌────────────────────────────────────────────────────────────────┐
│ ← Back to Upload         Review Classification    Step 3b of 7│
├────────────────────────────────────────────────────────────────┤
│                                                                │
│  Verify document types (AI suggestions shown)                 │
│                                                                │
│  ┌────────────────────────────────────────────────────────┐   │
│  │ 📄 meeting-notes.txt                                   │   │
│  │                                                        │   │
│  │ Document Type:                                         │   │
│  │ ┌──────────────────────────────────────────────────┐   │   │
│  │ │ ▼ Meeting Notes (AI: 94% confident)              │   │   │
│  │ └──────────────────────────────────────────────────┘   │   │
│  │                                                        │   │
│  │ AI Reasoning:                                          │   │
│  │ • Contains phrases: "steering committee", "action"     │   │
│  │ • Structure matches meeting note format                │   │
│  │ • Keywords: architecture, discussion, decisions        │   │
│  └────────────────────────────────────────────────────────┘   │
│                                                                │
│  [Additional documents shown similarly...]                     │
│                                                                │
│  ✓ All documents classified                                   │
│                                                                │
│                                     [Confirm & Process →]     │
│                                                                │
└────────────────────────────────────────────────────────────────┘
```

**Key Elements**:
- **Dropdown**: Override AI classification if wrong
- **AI Reasoning**: Explain why this classification (transparency)
- **Skip if High Confidence**: Only show if any doc < 90%

**Access Depth**: 4 clicks (conditional - only if needed)

---

#### **STEP 5: Processing**

**Purpose**: Show system is working, build anticipation

**Layout**:
```
┌────────────────────────────────────────────────────────────────┐
│                       Processing Pipeline         Step 4 of 7  │
├────────────────────────────────────────────────────────────────┤
│                                                                │
│  Analyzing your documents...                                   │
│                                                                │
│  ┌────────────────────────────────────────────────────────┐   │
│  │                                                        │   │
│  │        ✓ Parsed Documents (4 of 4)                     │   │
│  │        ✓ Extracted Text & Structure                    │   │
│  │        ⏳ Extracting Fields (2 of 4 complete)          │   │
│  │           • Key Findings ✓                             │   │
│  │           • Financial Health ✓                         │   │
│  │           • Staffing ⏳ (analyzing...)                  │   │
│  │           • Security Posture ⏸ (pending)               │   │
│  │        ⏸ Aggregating Results                           │   │
│  │        ⏸ Generating Deliverable                        │   │
│  │                                                        │   │
│  │        [████████████░░░░░░░░] 65%                      │   │
│  │                                                        │   │
│  └────────────────────────────────────────────────────────┘   │
│                                                                │
│  Estimated time remaining: ~30 seconds                         │
│                                                                │
│  💡 Tip: Meridian extracts data using passage-level citations  │
│     so you can verify every value later.                       │
│                                                                │
│                                           [View Live Log ↓]   │
│                                                                │
└────────────────────────────────────────────────────────────────┘
```

**Key Elements**:
- **Phase Indicators**: ✓ (done), ⏳ (active), ⏸ (pending)
- **Granular Progress**: Show individual fields being extracted
- **Progress Bar**: Visual completion indicator
- **Time Estimate**: Manage expectations
- **Educational Tips**: Build trust while waiting
- **Live Log**: Optional detail for power users

**Interaction**:
- **Auto-advance**: When complete, auto-navigate to Review
- **Background Processing**: User can leave and return (progress saved)
- **Cancel Option**: Allow abort with confirmation

**Access Depth**: 4 clicks from Home (automatic)

---

#### **STEP 6: Review Fields** (Core Experience)

**Purpose**: Review extracted data with evidence, resolve conflicts

**Layout** (Split-pane design):
```
┌────────────────────────────────────────────────────────────────┐
│ Enterprise Arch Review - Synapse     ✓ Processing Complete    │
├───────────────────┬────────────────────────────────────────────┤
│ Fields            │ Preview & Evidence                         │
│                   │                                            │
│ ⚠️ 1 Conflict     │ Key Findings                              │
│                   │ ┌────────────────────────────────────────┐ │
│ ▼ Key Findings    │ │ "Minor finding: log retention policy   │ │
│   ███ 94%         │ │ currently 10 months (Arcadia standard  │ │
│   3 sources       │ │ requires 12); remediation planned by   │ │
│                   │ │ 2025-12-31."                           │ │
│ ▼ Financial Health│ │                                        │ │
│   ⚠️ Conflict     │ │ 📄 cybersecurity-assessment.txt, p.1   │ │
│   ▓▓░ 78%        │ │ [View Full Source →]                   │ │
│   2 sources       │ └────────────────────────────────────────┘ │
│                   │                                            │
│ ▼ Staffing        │ Financial Health ⚠️                        │
│   ███ 92%         │ ┌────────────────────────────────────────┐ │
│   1 source        │ │ Multiple values found:                 │ │
│                   │ │                                        │ │
│ ▼ Security Posture│ │ ◉ $47.2 million USD                    │ │
│   ███ 96%         │ │   📄 vendor-prescreen.txt, p.1         │ │
│   2 sources       │ │   Confidence: High (89%)               │ │
│                   │ │   [Select This]                        │ │
│                   │ │                                        │ │
│                   │ │ ○ Approximately $45-50M                │ │
│                   │ │   📄 customer-bulletin.txt, p.1        │ │
│                   │ │   Confidence: Medium (72%)             │ │
│                   │ │   [Select This]                        │ │
│                   │ │                                        │ │
│                   │ │ [Use Both Values] [Override Manually]  │ │
│                   │ └────────────────────────────────────────┘ │
│                   │                                            │
│ [Save Progress]   │                    [Continue to Preview →]│
├───────────────────┴────────────────────────────────────────────┤
│ 💡 All conflicts resolved? Ready to finalize.                 │
└────────────────────────────────────────────────────────────────┘
```

**Key Elements**:

**Left Pane - Field Tree**:
- **Hierarchical List**: Collapsible sections
- **Confidence Indicators**: Bar visualization (████ = high, ▓▓░ = medium)
- **Conflict Badge**: ⚠️ red badge on fields needing attention
- **Source Count**: "3 sources" builds confidence
- **Warning Banner**: "1 Conflict" at top with count

**Right Pane - Evidence & Preview**:
- **Field Value**: Large, readable text
- **Source Attribution**: Document name, page, link to original
- **Conflict Resolution**: Radio selection with confidence scores
- **Evidence Drawer**: Click to see full passage with highlighting
- **Actions**: Select alternative value or manually override

**Access Depth**: 5 clicks from Home

---

#### **Tier 2: Secondary Actions** (One Click Away)

These are accessed from the main flow but are optional or conditional.

##### **Evidence Drawer** (Slide-in from right)

**Trigger**: Click confidence bars or "View Evidence" link

**Layout**:
```
┌────────────────────────────────────────────────────────────────┐
│                                     Evidence Drawer        [×] │
├────────────────────────────────────────────────────────────────┤
│                                                                │
│  Field: Financial Health                                       │
│  Current Value: $47.2 million USD                             │
│                                                                │
│  ┌──────────────────────────────────────────────────────┐     │
│  │ Source: vendor-prescreen.txt                         │     │
│  │ Page 1, Section: Financial Snapshot                  │     │
│  │                                                      │     │
│  │ ┌────────────────────────────────────────────────┐   │     │
│  │ │ "Primary contact: Jordan Kim (Director of      │   │     │
│  │ │ Enterprise Accounts). Financial snapshot:      │   │     │
│  │ │ FY2024 revenue reported as ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓  │   │     │
│  │ │ USD; staffing count 150..."                    │   │     │
│  │ └────────────────────────────────────────────────┘   │     │
│  │                      ▲ Highlighted match             │     │
│  │                                                      │     │
│  │ Extracted: 2 minutes ago                             │     │
│  │ Confidence: High (89%)                               │     │
│  │                                                      │     │
│  │ [View Full Document →] [Copy Text] [Report Issue]   │     │
│  └──────────────────────────────────────────────────────┘     │
│                                                                │
│  Alternative Sources:                                          │
│                                                                │
│  ┌──────────────────────────────────────────────────────┐     │
│  │ customer-bulletin.txt (Medium confidence)            │     │
│  │ "...approximately $45-50M based on preliminary..."   │     │
│  │ [View Details ↓]                                     │     │
│  └──────────────────────────────────────────────────────┘     │
│                                                                │
│  [Close]                                [Use Alternative →]   │
│                                                                │
└────────────────────────────────────────────────────────────────┘
```

**Key Features**:
- **Highlighted Text**: Yellow background on matched span
- **Full Context**: Surrounding text for clarity
- **Source Metadata**: Page, section, timestamp
- **Actions**: View original, copy, report issues
- **Alternative Views**: See other candidate values

**Access Depth**: 6 clicks (from Review Fields)

---

##### **Manual Override Modal**

**Trigger**: Click "Override Manually" on conflicted field

**Layout**:
```
┌────────────────────────────────────────────────────────────────┐
│              Override Field Value                          [×] │
├────────────────────────────────────────────────────────────────┤
│                                                                │
│  Field: Financial Health                                       │
│                                                                │
│  Current AI-extracted values:                                  │
│  • $47.2 million USD (89% confidence)                         │
│  • Approximately $45-50M (72% confidence)                     │
│                                                                │
│  Enter custom value:                                           │
│  ┌────────────────────────────────────────────────────────┐   │
│  │ $47.2 million USD                                      │   │
│  └────────────────────────────────────────────────────────┘   │
│                                                                │
│  Justification (recommended):                                  │
│  ┌────────────────────────────────────────────────────────┐   │
│  │ Verified with vendor's audited financial statement     │   │
│  └────────────────────────────────────────────────────────┘   │
│                                                                │
│  ⚠️ Warning: Manual overrides bypass AI verification          │
│                                                                │
│                                  [Cancel] [Save Override]     │
│                                                                │
└────────────────────────────────────────────────────────────────┘
```

**Access Depth**: 6 clicks (from Review Fields)

---

#### **STEP 7: Preview & Finalize**

**Purpose**: See final deliverable, make last adjustments

**Layout**:
```
┌────────────────────────────────────────────────────────────────┐
│ ← Back to Review        Final Preview & Export    Step 6 of 7 │
├────────────────────────────────────────────────────────────────┤
│                                                                │
│  Preview                                    [Markdown] [PDF]   │
│  ┌────────────────────────────────────────────────────────┐   │
│  │ ## Enterprise Architecture Readiness Review            │   │
│  │                                                        │   │
│  │ ### Key Findings:                                      │   │
│  │ "Minor finding: log retention policy currently 10      │   │
│  │ months (Arcadia standard requires 12); remediation     │   │
│  │ planned by 2025-12-31."[^1]                           │   │
│  │                                                        │   │
│  │ ### Financial Health:                                  │   │
│  │ "$47.2 million USD"[^2]                               │   │
│  │                                                        │   │
│  │ ### Staffing:                                          │   │
│  │ "150"[^3]                                             │   │
│  │                                                        │   │
│  │ ### Security Posture:                                  │   │
│  │ "ISO 27001 certification, 24/7 support desk, regional │   │
│  │ data residency in NA and EU"[^4]                      │   │
│  │                                                        │   │
│  │ ---                                                    │   │
│  │ [^1]: cybersecurity-assessment.txt: "Cybersecurity... │   │
│  │ [^2]: vendor-prescreen.txt: "Vendor Prescreen...      │   │
│  │ [^3]: vendor-prescreen.txt: "Vendor Prescreen...      │   │
│  │ [^4]: vendor-prescreen.txt: "Vendor Prescreen...      │   │
│  └────────────────────────────────────────────────────────┘   │
│                                                                │
│  ✓ All fields extracted                                        │
│  ✓ All conflicts resolved                                      │
│  ✓ 4 source documents processed                                │
│                                                                │
│  [← Edit Fields] [Download Markdown] [Download PDF] [Share →]│
│                                                                │
└────────────────────────────────────────────────────────────────┘
```

**Key Elements**:
- **Live Preview**: Rendered markdown with footnotes
- **Format Toggle**: Switch between MD and PDF view
- **Checklist**: Confidence summary before export
- **Multiple Actions**: Edit, download, share
- **Citations**: Footnotes link back to source documents

**Access Depth**: 6 clicks from Home

---

#### **STEP 8: Download & Share**

**Purpose**: Export deliverable in various formats

**Layout**:
```
┌────────────────────────────────────────────────────────────────┐
│                 Export Deliverable                         [×] │
├────────────────────────────────────────────────────────────────┤
│                                                                │
│  Your deliverable is ready!                                    │
│                                                                │
│  ┌────────────────────────────────────────────────────────┐   │
│  │ 📄 Enterprise Architecture Review - Synapse            │   │
│  │    Generated: October 22, 2025 at 2:51 PM             │   │
│  │    4 source documents • 4 fields extracted             │   │
│  └────────────────────────────────────────────────────────┘   │
│                                                                │
│  Download As:                                                  │
│  ┌──────────────────┐  ┌──────────────────┐                   │
│  │ 📄 Markdown      │  │ 📕 PDF           │                   │
│  │ (.md file)       │  │ (Formatted)      │                   │
│  │ [Download]       │  │ [Download]       │                   │
│  └──────────────────┘  └──────────────────┘                   │
│                                                                │
│  Share:                                                        │
│  ┌────────────────────────────────────────────────────────┐   │
│  │ 🔗 Public Link                                         │   │
│  │ https://meridian.local/share/019a0d1b-4644...         │   │
│  │ [Copy Link] [Configure Privacy →]                     │   │
│  └────────────────────────────────────────────────────────┘   │
│                                                                │
│  [← Back to Preview]                      [Create New Project]│
│                                                                │
└────────────────────────────────────────────────────────────────┘
```

**Access Depth**: 7 clicks from Home

---

#### **Tier 3: Advanced/Settings** (Two+ Clicks Away)

These are for power users and configuration.

##### **Analysis Type Management**

**Access**: Settings → Analysis Types

**Purpose**: Create custom analysis templates, modify existing

**Layout**:
```
┌────────────────────────────────────────────────────────────────┐
│ ← Settings          Analysis Types                             │
├────────────────────────────────────────────────────────────────┤
│                                                                │
│  Manage analysis type templates              [+ Create New]   │
│                                                                │
│  ┌────────────────────────────────────────────────────────┐   │
│  │ 🏢 Enterprise Architecture Readiness Review            │   │
│  │    4 output fields • Used in 12 projects               │   │
│  │    [Edit] [Duplicate] [Archive]                        │   │
│  └────────────────────────────────────────────────────────┘   │
│                                                                │
│  ┌────────────────────────────────────────────────────────┐   │
│  │ 📊 Vendor Due Diligence Report                         │   │
│  │    8 output fields • Used in 47 projects               │   │
│  │    [Edit] [Duplicate] [Archive]                        │   │
│  └────────────────────────────────────────────────────────┘   │
│                                                                │
└────────────────────────────────────────────────────────────────┘
```

**Access Depth**: 2 clicks (Settings → Analysis Types)

---

##### **Source Type Management**

**Access**: Settings → Source Types

**Purpose**: Define document types, classification rules

**Layout**:
```
┌────────────────────────────────────────────────────────────────┐
│ ← Settings          Source Types                               │
├────────────────────────────────────────────────────────────────┤
│                                                                │
│  Document classification templates          [+ Create New]    │
│                                                                │
│  ┌────────────────────────────────────────────────────────┐   │
│  │ 📋 Meeting Notes                                       │   │
│  │    Tags: meeting, architecture, decisions              │   │
│  │    Signal Phrases: "steering committee", "action"      │   │
│  │    [Edit] [Test Classification]                        │   │
│  └────────────────────────────────────────────────────────┘   │
│                                                                │
│  [Additional source types...]                                  │
│                                                                │
└────────────────────────────────────────────────────────────────┘
```

**Access Depth**: 2 clicks (Settings → Source Types)

---

## Mobile Considerations

### Responsive Breakpoints

- **Desktop**: 1280px+ (Full split-pane experience)
- **Tablet**: 768-1279px (Collapsible panes, stacked on demand)
- **Mobile**: <768px (Single column, tab-based navigation)

### Mobile-Specific Adaptations

**Upload Step**:
- Show native file picker (no drag-drop)
- One file at a time (better for small screens)
- Classification shown as expandable cards

**Review Step**:
- Tabs instead of split-pane: [Fields] [Preview] [Evidence]
- Swipe between fields
- Bottom sheet for evidence drawer

**Priority**: Desktop-first (primary use case is professional workstation)

---

## Accessibility Compliance

### WCAG 2.1 Level AA Requirements

1. **Color Contrast**: 4.5:1 for text, 3:1 for UI components
2. **Keyboard Navigation**: All actions accessible via Tab/Enter/Escape
3. **Screen Reader Support**: ARIA labels, semantic HTML
4. **Focus Indicators**: Visible 2px outline on all interactive elements
5. **Error Messages**: Associated with fields via aria-describedby

### Specific Implementations

- **Confidence Indicators**: Include aria-label "94% confidence, high"
- **File Upload**: Keyboard-accessible with Enter to trigger picker
- **Evidence Drawer**: Focus trap when open, Escape to close
- **Progress Indicators**: aria-live="polite" for status updates

---

## Information Density Management

### Progressive Disclosure Strategy

**Level 1** (Always Visible):
- Project name, current step, primary action
- Essential field values, confidence scores
- Critical warnings (conflicts, errors)

**Level 2** (Hover/Click):
- Evidence passages, source attribution
- Alternative values, merge reasoning
- Metadata (timestamps, version info)

**Level 3** (Advanced Settings):
- Schema editing, merge rule configuration
- Classification discriminators
- System logs, debug information

---

## Navigation Patterns

### Breadcrumb Trail

Always visible at top:
```
Home > Projects > Enterprise Arch Review > Review Fields
```

### Context Preservation

- **Browser Back**: Returns to previous step with state preserved
- **Auto-save**: Every 30 seconds or on step change
- **Resume Token**: Deep link to exact step in workflow

### Exit Points

1. **Save Draft**: Preserve progress, exit to dashboard
2. **Cancel Pipeline**: Confirmation modal, delete all data
3. **Pause Processing**: Stop job, allow resume later

---

## Search & Discovery

### Global Search Bar

**Scope**: Projects, documents, extracted values

**Features**:
- Type-ahead suggestions
- Recent searches
- Filters: Status, Date, Analysis Type

**Layout**:
```
┌────────────────────────────────────────────────┐
│ 🔍 Search projects, documents, values...      │
│                                                │
│ Recent:                                        │
│ • Enterprise Architecture Review               │
│ • Vendor Due Diligence Q3                     │
│                                                │
│ Quick Filters:                                 │
│ [In Progress] [Complete] [Has Conflicts]      │
└────────────────────────────────────────────────┘
```

---

## Conclusion

This information architecture provides:

1. **Linear Primary Flow**: 7 clear steps from Home to Deliverable
2. **Tiered Complexity**: 80% of users never leave Tier 1
3. **Evidence-First Design**: Trust through transparency at every step
4. **Mobile-Ready**: Responsive patterns for all screen sizes
5. **Accessible**: WCAG 2.1 AA compliant throughout

The structure mirrors the ScenarioA user story, transforming a complex technical process (document analysis, RAG, field extraction) into a guided, confidence-building journey that any enterprise user can complete in 8-12 minutes.

**Next Steps**: Proceed to detailed page layouts (Document 02) and component specifications (Document 03).
