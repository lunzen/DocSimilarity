# DocGrouping Application Guide

Complete technical documentation for the DocGrouping system — a document similarity analysis and grouping platform built on .NET 10, Blazor Server, PostgreSQL, and PDF.js.

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Project Structure](#2-project-structure)
3. [Getting Started](#3-getting-started)
4. [Domain Model](#4-domain-model)
5. [Database Layer](#5-database-layer)
6. [Text Processing Pipeline](#6-text-processing-pipeline)
7. [Grouping Pipeline](#7-grouping-pipeline)
8. [Rules Engine](#8-rules-engine)
9. [PDF Features](#9-pdf-features)
10. [Document Generator](#10-document-generator)
11. [Web UI](#11-web-ui)
12. [API Reference](#12-api-reference)
13. [Multi-Database Support](#13-multi-database-support)
14. [Configuration Reference](#14-configuration-reference)
15. [Performance & Scaling](#15-performance--scaling)

---

## 1. Architecture Overview

DocGrouping is a **Blazor Server + ASP.NET Core Web API** application that ingests documents (PDF and text), extracts and normalizes their text content, computes similarity fingerprints, and groups documents by content similarity across configurable confidence tiers.

### High-Level Data Flow

```
Raw PDF/Text Files
      |
      v
[Ingestion Service]
   - PDF text extraction (PdfPig)
   - Null byte stripping
   - UTF-8 encoding
      |
      v
[Text Normalization]
   - Unicode normalization
   - Lowercasing
   - OCR error correction
   - Page number / artifact removal
   - Whitespace / punctuation normalization
   - Hyphenation handling
      |
      v
[Fingerprinting]
   - Text Hash (SHA-256 of normalized text)
   - Fuzzy Hash (SHA-256 of top-50 token signature)
   - MinHash Signature (100-dimensional LSH fingerprint)
      |
      v
[Four-Phase Grouping]
   Phase 1: Exact text hash match     -> VeryHigh confidence
   Phase 2: Fuzzy hash match          -> High confidence
   Phase 3: LSH + Jaccard similarity  -> Medium/High confidence
   Phase 4: Remaining -> singletons   -> None confidence
      |
      v
[Confidence-Tiered Groups]
   Stored in PostgreSQL with canonical document selection,
   similarity scores, and match reasons
```

### Technology Stack

| Layer | Technology |
|-------|-----------|
| Frontend | Blazor Server, Bootstrap 5, Bootstrap Icons |
| PDF Rendering | PDF.js (client-side), pixel-based diff comparison |
| Backend | ASP.NET Core 10 Web API |
| Database | PostgreSQL 16 via EF Core 10 (Npgsql) |
| PDF Text Extraction | PdfPig (UglyToad.PdfPig) |
| Logging | Serilog (Console + File sinks) |
| Text Hashing | SHA-256, MinHash (100 universal hashes), LSH (20 bands x 5 rows) |

---

## 2. Project Structure

```
DocGrouping/
├── docs/                              # Documentation
│   ├── APPLICATION-GUIDE.md           # This file
│   ├── EXECUTIVE-OVERVIEW.md          # Product overview for stakeholders
│   └── SIMILARITY-CALCULATIONS.md     # Deep-dive on similarity math
│
├── src/
│   ├── DocGrouping.Domain/            # Entities, enums, interfaces (no dependencies)
│   │   ├── Entities/
│   │   │   ├── Document.cs            # Core document entity
│   │   │   ├── DocumentGroup.cs       # Group with confidence tier
│   │   │   ├── DocumentGroupMembership.cs  # Many-to-many join
│   │   │   ├── MinHashSignature.cs    # 100-element LSH signature
│   │   │   ├── LshBucket.cs           # LSH band bucket assignments
│   │   │   ├── BusinessRule.cs        # Configurable grouping rules
│   │   │   └── ProcessingJob.cs       # Background job tracking
│   │   ├── Enums/
│   │   │   ├── MatchConfidence.cs     # Exact, VeryHigh, High, Medium, None
│   │   │   ├── RuleType.cs            # 13 rule types
│   │   │   ├── RuleAction.cs          # ForceGroup, PreventGroup, SetCanonical, Separate
│   │   │   └── JobStatus.cs           # Pending, Running, Completed, Failed, Cancelled
│   │   ├── Interfaces/                # Repository contracts
│   │   └── Projections/               # Lightweight query models
│   │
│   ├── DocGrouping.Application/       # DTOs, service interfaces
│   │   ├── DTOs/                      # DocumentDto, GroupDto, etc.
│   │   └── Interfaces/                # IDocumentIngestionService, IGroupingOrchestrator, etc.
│   │
│   ├── DocGrouping.Infrastructure/    # Implementation layer
│   │   ├── Configuration/             # Options classes (GroupingThresholds, PdfStorageOptions)
│   │   ├── Persistence/
│   │   │   ├── DocGroupingDbContext.cs # EF Core context
│   │   │   ├── Configurations/        # Fluent API entity configs
│   │   │   └── Repositories/          # Repository implementations
│   │   ├── TextProcessing/
│   │   │   ├── TextNormalizer.cs       # 8-step text normalization
│   │   │   ├── DocumentFingerprinter.cs # Hash generation + similarity metrics
│   │   │   ├── MinHashLshIndex.cs     # In-memory LSH index
│   │   │   ├── DiffEngine.cs          # LCS-based diff + word stats
│   │   │   └── PdfTextExtractor.cs    # PdfPig wrapper
│   │   ├── Services/
│   │   │   ├── DocumentIngestionService.cs  # File -> DB pipeline
│   │   │   ├── GroupingOrchestrator.cs      # 4-phase grouping engine
│   │   │   ├── DocumentGeneratorService.cs  # Synthetic document generation
│   │   │   └── PdfStorageService.cs         # PDF file storage on disk
│   │   └── Rules/
│   │       └── RulesEngine.cs         # Business rule evaluation
│   │
│   └── DocGrouping.Web/              # Blazor Server + API host
│       ├── Components/
│       │   ├── Layout/                # MainLayout, NavMenu
│       │   ├── Pages/                 # Upload, Results, Canonicals, Generator,
│       │   │                          # Comparison, Rules, Settings
│       │   └── Shared/                # GroupCard, ConfidenceBadge, DiffSummary,
│       │                              # PdfCompareView, MatchExplanation, etc.
│       ├── Controllers/               # DocumentsController, GroupsController, RulesController
│       ├── Middleware/                 # DatabaseSelectionMiddleware
│       ├── Services/                  # DatabaseSelectorState, ConnectionResolver, Initializer
│       ├── wwwroot/
│       │   ├── css/app.css            # Custom styles
│       │   ├── js/pdfhighlight.js     # Pixel-based PDF diff comparison
│       │   ├── js/pdfsync.js          # PDF scroll synchronization
│       │   └── lib/pdfjs/             # PDF.js library (pdf.min.mjs, pdf.worker.min.mjs)
│       ├── Program.cs                 # App startup, DI configuration
│       └── appsettings.json           # Connection strings, thresholds, database list
│
├── test/
│   ├── DocGrouping.Domain.Tests/
│   ├── DocGrouping.Infrastructure.Tests/
│   └── DocGrouping.Integration.Tests/
│
└── test-pdfs/                         # Sample PDF files for testing
```

---

## 3. Getting Started

### Prerequisites

- **.NET 10 SDK** (10.0.100+)
- **PostgreSQL 16** (recommended via Docker)
- **Node.js** is NOT required — PDF.js is bundled as static files

### Database Setup

The easiest way to run PostgreSQL:

```bash
docker run -d \
  --name docgrouping-postgres \
  -e POSTGRES_USER=postgres \
  -e POSTGRES_PASSWORD=postgres \
  -p 5432:5432 \
  postgres:16
```

The application uses `EnsureCreatedAsync()` to auto-create the schema on first run. No migrations are needed.

### Running the Application

```bash
dotnet run --project src/DocGrouping.Web
```

The app starts at `http://localhost:5053` by default.

### Configuration

Edit `src/DocGrouping.Web/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=docgrouping;Username=postgres;Password=postgres"
  },
  "Databases": ["docgrouping", "docgrouping_pilot"],
  "PdfStorage": {
    "RootPath": "C:/Temp/DocGrouping/uploads"
  },
  "GroupingThresholds": {
    "MediumMinJaccard": 0.50,
    "HighMinJaccard": 0.85,
    "FuzzyHashAssumedSimilarity": 0.90,
    "MinHashPrefilterThreshold": 0.35
  }
}
```

---

## 4. Domain Model

### Core Entities

#### Document

The primary entity representing an ingested document.

| Field | Type | Description |
|-------|------|-------------|
| `Id` | Guid | Primary key |
| `FileName` | string | Original file name |
| `FilePath` | string | Source file path |
| `FileSizeBytes` | long | Raw file size |
| `FileHash` | string | SHA-256 of raw file bytes |
| `OriginalText` | string | Text as extracted (before normalization) |
| `NormalizedText` | string | Text after full normalization pipeline |
| `TextHash` | string | SHA-256 of normalized text |
| `FuzzyHash` | string | SHA-256 of top-50 token signature |
| `WordCount` | int | Token count in normalized text |
| `DocumentType` | string? | Optional classification label |
| `DocumentDate` | DateTime? | Optional document date |
| `Parties` | string? | JSON array of party names |
| `Tags` | string? | JSON array of tags |
| `CustomMetadata` | string? | JSON object of arbitrary metadata |
| `BatesRange` | string? | Bates stamp range (e.g., "ABC001-ABC005") |
| `SourceFolder` | string? | Originating folder path |
| `IsCanonicalReference` | bool | Whether this is a golden reference document |
| `CreatedAt` | DateTime | Ingestion timestamp |

**Navigations:** `GroupMembership`, `MinHashSignature`, `LshBuckets`

#### DocumentGroup

A group of related documents with a confidence assessment.

| Field | Type | Description |
|-------|------|-------------|
| `Id` | Guid | Primary key |
| `GroupNumber` | int | Sequential group number (display) |
| `Confidence` | MatchConfidence | VeryHigh, High, Medium, or None |
| `MatchReason` | string | Human-readable explanation |
| `CanonicalDocumentId` | Guid? | Points to the representative document |
| `DocumentCount` | int | Number of members |

**Navigations:** `CanonicalDocument`, `Memberships`

#### DocumentGroupMembership

Join table linking documents to groups.

| Field | Type | Description |
|-------|------|-------------|
| `DocumentId` | Guid | FK to Document |
| `GroupId` | Guid | FK to DocumentGroup |
| `IsCanonical` | bool | Whether this member is the canonical |
| `SimilarityScore` | decimal | Match score (0.0 to 1.0) |

#### MinHashSignature

Stored LSH fingerprint for a document.

| Field | Type | Description |
|-------|------|-------------|
| `DocumentId` | Guid | FK to Document |
| `Signature` | int[] | 100-element MinHash array |

#### LshBucket

Band-level bucket assignments for LSH candidate generation.

| Field | Type | Description |
|-------|------|-------------|
| `DocumentId` | Guid | FK to Document |
| `BandIndex` | int | Band number (0-19) |
| `BucketHash` | long | FNV-1a hash of the band slice |

#### BusinessRule

Configurable rules that modify grouping behavior.

| Field | Type | Description |
|-------|------|-------------|
| `RuleId` | string | Human-readable ID (e.g., "custom_abc123") |
| `Name` | string | Display name |
| `RuleType` | RuleType | One of 13 rule types |
| `Action` | RuleAction | ForceGroup, PreventGroup, SetCanonical, or Separate |
| `Priority` | int | Execution order (lower = first) |
| `Enabled` | bool | Toggle on/off |
| `Conditions` | JsonDocument? | Rule-specific parameters |

### Enums

**MatchConfidence:** `Exact`, `VeryHigh`, `High`, `Medium`, `None`

**RuleType:** `VersionPriority`, `MetadataSeparator`, `DateThreshold`, `RedactionSeparator`, `BatesSignificance`, `DocumentTypeSeparator`, `SourceFolderSeparator`, `SameParties`, `SameBatesRange`, `DateProximity`, `TagMatch`, `AlwaysGroup`, `NeverGroup`

**RuleAction:** `ForceGroup`, `PreventGroup`, `SetCanonical`, `Separate`

---

## 5. Database Layer

### DbContext

`DocGroupingDbContext` is configured for PostgreSQL via Npgsql. It uses fluent API configurations in `Persistence/Configurations/` for index definitions, column types, and JSON storage.

Key indexes:
- `Document.TextHash` — used in Phase 1 hash grouping
- `Document.FuzzyHash` — used in Phase 2 fuzzy grouping
- `LshBucket(BandIndex, BucketHash)` — used in Phase 3 LSH candidate lookup

### Multi-Database Architecture

The DbContext is registered with a **factory** in `Program.cs` that resolves the connection string per request:

1. `DatabaseSelectionMiddleware` reads `X-Database` header or `?db=` query param from each HTTP request
2. The database name is stored in `HttpContext.Items["ActiveDatabase"]`
3. The DbContext factory reads this value (falling back to `DatabaseSelectorState.CurrentDatabase`)
4. `DatabaseConnectionResolver` builds the connection string by replacing the database name in the template

This enables seamless switching between databases (e.g., production vs. pilot) from the UI dropdown or API headers.

### Repositories

All repositories follow a standard pattern with `AsNoTracking()` for read operations and batch support for writes.

| Repository | Key Methods |
|-----------|-------------|
| `DocumentRepository` | `GetByTextHashesAsync`, `GetByFuzzyHashesAsync`, `GetUngroupedAsync`, `AddRangeAsync` |
| `DocumentGroupRepository` | `GetPagedAsync`, `AddRangeAsync`, `DeleteAllAsync`, `GetNextGroupNumberAsync` |
| `BusinessRuleRepository` | Standard CRUD |
| `ProcessingJobRepository` | `GetRecentAsync`, job status tracking |

### EF Core Concurrency Handling

The application includes specific fixes for EF Core navigation fix-up issues:

- **Phantom-Modified detection:** `DocumentGroupRepository.UpdateAsync` resets memberships with identical original/current values to `Unchanged` before `SaveChangesAsync`
- **Change tracker clearing:** `GroupingOrchestrator` calls `ChangeTracker.Clear()` between batch saves and at phase boundaries to prevent stale entity accumulation
- **No-tracking reads:** Repository query methods use `AsNoTracking()` with explicit `Include()` to avoid unintended tracking

---

## 6. Text Processing Pipeline

### 6.1 Text Extraction

**PDF files** are processed by `PdfTextExtractor` (wrapping PdfPig/UglyToad). Text is extracted page-by-page and concatenated.

**Text files** are decoded as UTF-8.

Both paths strip null bytes (`\0`) before storage — PostgreSQL rejects null bytes in text columns.

### 6.2 Text Normalization

`TextNormalizer.Normalize()` applies an 8-step pipeline:

| Step | Operation | Purpose |
|------|-----------|---------|
| 1 | Unicode normalization (NFKC) | Handles ligatures (`ﬁ` → `fi`), fullwidth chars. Uses `Rune` API to strip lone surrogates |
| 2 | Lowercasing | `ToLowerInvariant()` — case shouldn't affect similarity |
| 3 | OCR error correction | `0` → `o`, `1` → `l` in word context, `rn` → `m`, common substitutions |
| 4 | Page number removal | Strips `Page X of Y`, standalone numbers, page headers |
| 5 | Whitespace normalization | Collapses all whitespace to single spaces |
| 6 | Punctuation normalization | Smart quotes → ASCII, dashes normalized, decorative punctuation removed |
| 7 | Hyphenation handling | Rejoins end-of-line hyphenated words |
| 8 | Final whitespace collapse | Trim and collapse any remaining multi-spaces |

**Why normalization matters:** Two copies of the same document — one scanned, one born-digital — will have different raw text due to OCR errors, different page headers, and formatting artifacts. Normalization strips all of that so *content* is compared fairly.

### 6.3 Fingerprinting

`DocumentFingerprinter` generates three fingerprints per document:

#### Text Hash (SHA-256)
- Input: full normalized text
- Output: 64-character hex string
- Guarantee: identical hash = byte-for-byte identical normalized text
- Used in: Phase 1 grouping (VeryHigh confidence)

#### Fuzzy Hash (SHA-256 of top-K signature)
- Extract all tokens from normalized text
- Filter: minimum 6 characters, no stopwords, no digits, no currency symbols
- Count frequency of remaining tokens
- Take top 50 by frequency, sort alphabetically
- SHA-256 hash of the sorted list
- Guarantee: identical hash = same high-frequency vocabulary
- Used in: Phase 2 grouping (High confidence)

#### MinHash Signature (100-dimensional)
- 100 independent universal hash functions: `h_i(x) = (a_i * x + b_i) mod 2^31-1`
- Coefficients deterministically generated (seed=42)
- For each hash function, record the minimum hash value across all tokens
- Result: 100-element integer array
- Property: fraction of matching positions approximates Jaccard similarity
- Used in: Phase 3 LSH candidate generation

### 6.4 Similarity Metrics

`DocumentFingerprinter.CalculateSimilarityMetrics()` computes:

| Metric | Formula | Used For |
|--------|---------|----------|
| **Jaccard Similarity** | \|A ∩ B\| / \|A ∪ B\| | Primary grouping metric — drives all tier assignments |
| **Overlap Coefficient** | \|A ∩ B\| / min(\|A\|, \|B\|) | Displayed in UI — good for size-asymmetric comparisons |
| **Fuzzy Signature Jaccard** | Jaccard of top-50 word sets | Displayed in UI — measures topic-level similarity |
| **Common Token Count** | \|A ∩ B\| | Displayed in UI |

### 6.5 Diff Engine

`DiffEngine` provides two comparison modes:

**Line-level diff** (`ComputeDiff`): LCS-based diff producing `DiffLine` objects (Same, Added, Removed) with inline word-level spans for highlighting.

**Word statistics** (`ComputeWordStats`): Counts words added, removed, common, and computes a similarity percentage: `2 * common / (total1 + total2) * 100`.

---

## 7. Grouping Pipeline

### Full Grouping (`GroupAllDocumentsAsync`)

Deletes all existing groups and reprocesses every document through four phases.

#### Phase 1 — Text Hash (VeryHigh Confidence)

1. Group all documents by `TextHash`
2. Documents sharing a text hash have byte-identical normalized text
3. Evaluate business rules on each pair
4. Select canonical document (prefer `IsCanonicalReference`, then longest text)
5. Create `DocumentGroup` with `Confidence = VeryHigh`, `SimilarityScore = 1.0`
6. Batch save every 500 groups, clear change tracker

**Cost:** O(1) per document (hash table lookup)

#### Phase 2 — Fuzzy Hash (High Confidence)

1. Build fuzzy hash index on **ungrouped documents only**
2. Documents sharing a fuzzy hash have the same top-50 vocabulary
3. Evaluate business rules, select canonical
4. Create groups with `Confidence = High`, `SimilarityScore = 0.9` (configurable)
5. Batch save

**Cost:** O(1) per document

#### Phase 3 — LSH + Jaccard (Medium/High Confidence)

1. Collect remaining ungrouped documents
2. **Small sets (<200 docs):** Brute-force all pairs with exact Jaccard
3. **Large sets (200+ docs):**
   a. Generate MinHash signatures (100 hashes per document)
   b. Build LSH index (20 bands x 5 rows)
   c. Get candidate pairs (any shared LSH bucket)
   d. Pre-filter: skip pairs where estimated MinHash Jaccard < `MinHashPrefilterThreshold`
   e. Verify remaining pairs with exact Jaccard from full token sets
4. Pairs with Jaccard >= `HighMinJaccard` → High confidence
5. Pairs with Jaccard in [`MediumMinJaccard`, `HighMinJaccard`) → Medium confidence
6. Union-find merge to cluster verified pairs into groups
7. Batch save

**Cost:** O(n * k) via LSH, where k = average candidates per document

#### Phase 4 — Singletons (None Confidence)

1. All remaining ungrouped documents become singleton groups
2. Each gets `Confidence = None`
3. **Every document belongs to exactly one group** — "None" means "confirmed unique"

### Incremental Grouping (`GroupIncrementalAsync`)

Designed for Phase 2 strict mode where new documents are compared **only against existing groups**, never against each other.

1. Get count of existing groups (lightweight — no full load)
2. Get ungrouped documents
3. **Phase 1:** Match by text hash against existing grouped documents
4. **Phase 2:** Match by fuzzy hash against existing grouped documents
5. **Phase 3:** Match against canonical documents only (if any are set), via LSH
6. **Phase 4:** Remaining → singletons

**Constraint enforced:** New-to-new document comparison is explicitly removed. Unmatched new documents go straight to singletons.

### Classification Mode (`ClassifyAgainstCanonicalsAsync`)

Matches documents against a curated set of canonical reference documents:

1. Load all documents where `IsCanonicalReference = true`
2. Build LSH index of canonical documents
3. For each target document, find candidates via LSH
4. Verify with exact Jaccard
5. Return `CanonicalMatchResult` with matched canonical ID, confidence, and reason

---

## 8. Rules Engine

The rules engine evaluates business rules at grouping time to override or modify similarity-based decisions.

### Rule Evaluation

`RulesEngine.ShouldGroup(text1, metadata1, text2, metadata2, confidenceLevel)` returns a `GroupingDecision`:

- `ShouldGroup` (bool) — allow or prevent grouping
- `RuleModified` (bool) — whether any rule modified the default decision
- `AppliedRules` (list) — which rules fired
- `Explanation` (string) — human-readable reason

Rules are evaluated in priority order (lower number = higher priority). The first rule that triggers determines the outcome.

### Default Rules

| Priority | Rule | Type | Action | Default |
|----------|------|------|--------|---------|
| 1 | Version Priority | VersionPriority | SetCanonical | Enabled |
| 2 | Redaction Separator | RedactionSeparator | Separate | Enabled |
| 3 | Bates Significance | BatesSignificance | Separate | **Disabled** |
| 4 | Document Type Separator | DocumentTypeSeparator | Separate | Enabled |
| 5 | Source Folder Separator | SourceFolderSeparator | Separate | Enabled |
| 6-13 | Various metadata rules | Various | Various | Varies |

### Custom Rules

Users can create custom rules via the Rules page or API. Each rule specifies:
- **Rule type** — what kind of comparison to perform
- **Action** — what to do when the rule matches
- **Conditions** — JSON parameters specific to the rule type
- **Priority** — where in the evaluation order this rule falls

---

## 9. PDF Features

### PDF Storage

Original PDF files are saved to disk during ingestion:

- **Path pattern:** `{RootPath}/{dbName}/{documentId}.pdf`
- **Default root:** `C:/Temp/DocGrouping/uploads`
- **Service:** `PdfStorageService` implements `IPdfStorageService`
- **API:** `GET /api/documents/{id}/pdf?db={dbName}` serves the file as `application/pdf`

### PDF Viewing

The Results page and Comparison page both support inline PDF viewing:
- Results page: "View PDF" button per document in expanded GroupCard, opens an `<iframe>` (600px height)
- Comparison page: PDF Compare tab shows two PDFs side-by-side

### PDF Visual Comparison (`pdfhighlight.js`)

The PDF Compare tab uses **pixel-based diff comparison** to highlight differences between two PDFs. This approach was chosen because Chromium's PDF printer (used by the document generator) creates font subsets that lack proper `ToUnicode` CMap entries, making text extraction unreliable for bold text.

#### How it works:

1. **Render both PDFs** to HTML5 Canvas elements using PDF.js at the container's width, accounting for `devicePixelRatio`
2. **Compare pixels block-by-block** (10px CSS blocks):
   - Convert each pixel to luminance: `luma = R * 0.299 + G * 0.587 + B * 0.114`
   - If luminance difference > 80 (threshold), mark pixel as different
   - If > 20% of sampled pixels in a block differ, mark the block as changed
   - Sample every other pixel in each direction for performance
3. **Merge adjacent diff blocks** using flood-fill on an 8-connected grid to form larger highlight regions
4. **Draw highlights** on transparent overlay canvases:
   - Document 1: red fill (`rgba(220, 53, 69, 0.3)`) with red border
   - Document 2: green fill (`rgba(40, 167, 69, 0.3)`) with green border
   - Rounded rectangle shapes with 3px padding
5. **Synchronized scrolling** between the two PDF containers (percentage-based)

#### Key parameters:

| Parameter | Value | Purpose |
|-----------|-------|---------|
| `blockSize` | 10 CSS px | Granularity of comparison blocks |
| `lumaThreshold` | 80 | Luminance difference to count a pixel as changed |
| `blockDiffFraction` | 0.20 | Fraction of pixels in a block that must differ |

These values are tuned to detect real content differences (different names, dates, amounts) while filtering out sub-pixel font rendering noise.

---

## 10. Document Generator

The built-in generator creates synthetic documents for testing and demonstration.

### Templates (10 available)

Lease Agreement, Service Contract, Invoice, Property Deed, Shareholder Agreement, Employment Contract, Purchase Agreement, Promissory Note, Settlement Agreement, Compliance Report

### Generation Features

Each generated document can include:
- **Variable substitution** — random names, dates, amounts, addresses
- **Bates stamps** — sequential numbering (e.g., ABC001-ABC005)
- **Received stamps** — date/time received marks
- **Fax headers** — simulated fax transmission headers
- **Page numbers** — "Page X of Y" footers
- **Redactions** — blocked-out sensitive phrases (light or heavy)
- **OCR errors** — systematic character substitutions at configurable levels

### Bulk Generation

The Generator page supports generating up to 500,000 documents at once:
1. Set count and options (mark as canonical, grouping mode)
2. Click Generate — progress bar tracks ingestion
3. Documents are ingested and grouped automatically
4. Metrics display generation time, group statistics, phase breakdown

---

## 11. Web UI

### Pages

#### Upload (`/`)
The landing page for ingesting documents.
- Drag-and-drop file zone (PDF and TXT)
- Folder upload support
- Grouping mode toggle: **Batch** (group all) vs. **Classify Against Canonicals**
- Status bar showing existing document count
- Process Documents button triggers ingestion + grouping + navigation to Results

#### Results (`/results`)
The primary results dashboard.
- **KPI cards:** Total Documents, Groups, Duplicates Found, Deduplication Ratio
- **Confidence distribution bar:** visual breakdown of group confidence tiers
- **Filter buttons:** All, VeryHigh, High, Medium, None
- **Export:** JSON and CSV (current page)
- **GroupCard components:** expandable cards showing group members, similarity scores, PDF viewing, and Compare links
- **Pagination:** configurable page size

#### Comparison (`/comparison/{GroupNumber}`)
Side-by-side document comparison with four tabs:
1. **Side-by-Side** — two-column text diff with word-level highlighting
2. **Unified Diff** — traditional unified diff format with +/- coloring
3. **Summary** — word and segment statistics, key differences list
4. **PDF Compare** — visual PDF rendering with pixel-based diff overlay

Also displays:
- Similarity metrics (Jaccard, Overlap, Fuzzy Signature, Common Tokens)
- Metadata comparison table (file names, sizes, hashes, dates)
- Document selector dropdowns (for groups with >2 members)

#### Canonicals (`/canonicals`)
Manage canonical (golden reference) documents.
- KPI: canonical count, types covered, unclassified documents
- Document type filter
- Toggle individual documents as canonical
- Batch select and mark canonical
- Classify Against Canonicals button

#### Generator (`/generator`)
Create synthetic test documents.
- Bulk generation with count, canonical marking, and grouping mode
- Progress bar and live log feed
- Template preview tabs with per-template generation
- Metrics summary after generation completes

#### Rules (`/rules`)
Configure business rules that modify grouping behavior.
- List of rule cards with toggle, edit, delete
- Add Rule button opens modal editor
- Rule properties: name, type, action, priority, enabled, conditions JSON

#### Settings (`/settings`)
Configure similarity thresholds.
- Slider controls for all four thresholds
- Validation (MediumMin < HighMin, Prefilter < MediumMin)
- Save and Reset buttons
- Live tier preview showing how threshold changes affect classification
- Visual scale bar showing None/Medium/High regions

### Shared Components

| Component | Purpose |
|-----------|---------|
| `GroupCard` | Expandable group display with member table, PDF viewer, Compare link |
| `ConfidenceBadge` | Color-coded confidence label |
| `ConfidenceBar` | Stacked bar chart of confidence distribution |
| `MatchExplanation` | Four-column metric cards + keyword badges |
| `DiffSummary` | Word-level statistics and key differences list |
| `SideBySideView` | Two-column text diff with sync scrolling |
| `UnifiedDiffView` | Traditional unified diff display |
| `PdfCompareView` | Side-by-side PDF rendering with pixel diff |
| `MetadataComparison` | Side-by-side metadata table |
| `DatabaseSelector` | Database switching dropdown in nav bar |
| `KpiCard` | Metric display card (value, label, icon, color) |
| `LoadingSpinner` | Overlay spinner with message |

---

## 12. API Reference

All endpoints accept an optional `X-Database` header or `?db=` query parameter to select the target database.

### Documents (`/api/documents`)

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/upload` | Upload files (multipart form) — ingests and returns document list |
| `POST` | `/load-samples` | Ingest sample documents from configured directory |
| `POST` | `/process` | Run full grouping — returns stats and processing time |
| `GET` | `/` | List all documents with hash prefixes |
| `GET` | `/canonicals` | List canonical documents grouped by type |
| `POST` | `/{id}/set-canonical` | Toggle canonical status for a document |
| `POST` | `/classify-against-canonicals` | Run classification mode against canonicals |
| `GET` | `/{id}/pdf?db={dbName}` | Serve original PDF file |
| `POST` | `/generate-bulk` | Generate synthetic documents `{ count, markCanonical, mode }` |
| `POST` | `/process-incremental` | Run incremental grouping on ungrouped documents |

### Groups (`/api/groups`)

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/?page=1&pageSize=20&confidence=high` | Paginated group list with optional filter |
| `GET` | `/{groupNumber}` | Single group detail with similarity metrics |

### Rules (`/api/rules`)

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/` | List all rules ordered by priority |
| `GET` | `/{ruleId}` | Get single rule |
| `POST` | `/` | Create rule (auto-generates ruleId) |
| `PUT` | `/{ruleId}` | Update rule |
| `DELETE` | `/{ruleId}` | Delete rule |

---

## 13. Multi-Database Support

The application supports multiple isolated databases, each containing its own set of documents, groups, and rules.

### How It Works

1. **Configuration:** `appsettings.json` lists available databases:
   ```json
   "Databases": ["docgrouping", "docgrouping_pilot"]
   ```

2. **UI Selection:** The `DatabaseSelector` dropdown in the nav bar lets users switch databases. This updates `DatabaseSelectorState`, which triggers page refreshes.

3. **API Selection:** Pass `X-Database: docgrouping_pilot` header or `?db=docgrouping_pilot` query parameter on any API request.

4. **Resolution Chain:**
   - `DatabaseSelectionMiddleware` extracts database name from request → `HttpContext.Items`
   - DbContext factory reads from `HttpContext.Items` → falls back to `DatabaseSelectorState`
   - `DatabaseConnectionResolver` builds connection string from template
   - `DatabaseInitializer` ensures the database exists (auto-creates on first use)

### Use Cases

- **Production vs. Pilot:** Test with a pilot database before running on production data
- **Client Separation:** Isolate different clients' documents in separate databases
- **A/B Testing:** Compare grouping results with different threshold configurations

---

## 14. Configuration Reference

### `appsettings.json`

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=docgrouping;Username=postgres;Password=postgres"
  },
  "Databases": ["docgrouping", "docgrouping_pilot"],
  "PdfStorage": {
    "RootPath": "C:/Temp/DocGrouping/uploads"
  },
  "GroupingThresholds": {
    "MediumMinJaccard": 0.50,
    "HighMinJaccard": 0.85,
    "FuzzyHashAssumedSimilarity": 0.90,
    "MinHashPrefilterThreshold": 0.35
  },
  "Serilog": {
    "WriteTo": [
      { "Name": "Console" },
      { "Name": "File", "Args": { "path": "logs/docgrouping-.log", "rollingInterval": "Day" } }
    ]
  }
}
```

### Grouping Thresholds

| Parameter | Default | Range | Description |
|-----------|---------|-------|-------------|
| `MediumMinJaccard` | 0.50 | 0.0–1.0 | Minimum Jaccard to group at Medium confidence |
| `HighMinJaccard` | 0.85 | 0.0–1.0 | Minimum Jaccard to group at High confidence |
| `FuzzyHashAssumedSimilarity` | 0.90 | 0.0–1.0 | Score recorded for fuzzy hash matches |
| `MinHashPrefilterThreshold` | 0.35 | 0.0–1.0 | LSH candidate pre-filter (performance tuning) |

**Constraints:** `MinHashPrefilterThreshold` < `MediumMinJaccard` < `HighMinJaccard`

### PDF Storage

| Parameter | Default | Description |
|-----------|---------|-------------|
| `PdfStorage.RootPath` | `C:/Temp/DocGrouping/uploads` | Root directory for PDF file storage |

Files are stored as: `{RootPath}/{databaseName}/{documentId}.pdf`

---

## 15. Performance & Scaling

### Proven Scale

The system has been tested with **800,000 documents** successfully ingested and grouped.

### Performance Characteristics

| Phase | Complexity | Notes |
|-------|-----------|-------|
| Phase 1 (Text Hash) | O(n) | Hash table grouping |
| Phase 2 (Fuzzy Hash) | O(n) | Hash table grouping |
| Phase 3 (LSH) | O(n * k) | k = avg candidates per doc, typically << n |
| Phase 3 (Brute-force) | O(n^2) | Only used for <200 ungrouped docs |
| Phase 4 (Singletons) | O(n) | Simple assignment |

### Batch Processing

- Groups are saved in batches of 500 to avoid memory pressure
- EF Core change tracker is cleared between batches
- MinHash signatures are computed in-memory (not persisted for full grouping)

### LSH Tuning

The LSH configuration (20 bands x 5 rows) provides:

| Jaccard | Detection Probability |
|---------|----------------------|
| 0.70 | ~97.5% |
| 0.50 | ~56% |
| 0.30 | ~4.7% |

Degenerate LSH buckets (>100 documents) are skipped to prevent false positive explosions.

### Memory Considerations

- Full grouping loads all documents into memory for hash indexing
- For very large datasets, incremental grouping is preferred — processes only ungrouped documents
- Text normalization and fingerprinting are stateless and can be parallelized
