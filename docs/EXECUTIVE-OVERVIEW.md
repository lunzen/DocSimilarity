# DocGrouping — Executive Overview

## Intelligent Document Deduplication for Oil & Gas Operations

---

## The Problem

Oil and gas companies manage enormous volumes of documents across exploration, drilling, production, regulatory, and land operations. A single well can generate hundreds of documents — permits, surface use agreements, royalty statements, inspection reports, environmental compliance filings, and land title records. Across thousands of wells and decades of operations, these document repositories grow to millions of files and tens of terabytes.

**The core challenge:** The same document exists in many places, in many forms.

A drilling permit might appear as:
- The original born-digital filing
- A scanned copy in a county records archive
- A faxed version in a field office file
- A redacted copy produced during litigation
- Multiple versions with minor revisions (different effective dates, amended conditions)
- Copies with Bates stamps applied during legal discovery

These aren't byte-identical files. They have different page layouts, OCR artifacts, stamps, headers, and formatting. But they represent the same underlying document — or close variants of it.

Without automated deduplication, operators face:
- **Redundant storage costs** — paying to store and manage the same content many times over
- **Inconsistent records** — different versions of the same document leading to conflicting information
- **Manual review burden** — staff spending hours comparing documents that a system could match in seconds
- **Regulatory risk** — inability to identify the authoritative version of a compliance document
- **Discovery costs** — producing duplicate documents in litigation, increasing review time and expense

---

## The Solution

DocGrouping is an intelligent document analysis platform that automatically identifies duplicate and near-duplicate documents across large collections, regardless of format differences, OCR quality, or applied markings.

### How It Works

Documents flow through a multi-stage pipeline that moves from fast, exact methods to sophisticated similarity analysis:

**Stage 1 — Text Extraction & Normalization**
Every document's text is extracted and cleaned through a normalization pipeline that strips away formatting noise: OCR errors are corrected, page numbers and Bates stamps are removed, whitespace and punctuation are standardized. The result is a clean representation of the document's *content*, independent of how it was printed, scanned, or stamped.

**Stage 2 — Fingerprinting**
Each document receives three fingerprints:
- An **exact fingerprint** (cryptographic hash) that identifies byte-identical content
- A **fuzzy fingerprint** that captures the document's characteristic vocabulary, tolerating minor field-level changes
- A **similarity fingerprint** (MinHash) that enables fast approximate matching across millions of documents

**Stage 3 — Intelligent Grouping**
Documents are grouped through a cascade of matching strategies:
1. **Exact matches** — identical content after normalization (highest confidence)
2. **Near-duplicate matches** — same document template with different field values (high confidence)
3. **Similarity matches** — statistically similar content verified by mathematical comparison (medium confidence, flagged for review)
4. **Unique documents** — confirmed to have no close matches in the collection

**Stage 4 — Confidence-Tiered Results**
Every document is assigned to a group with a confidence tier:

| Tier | What It Means | Action Required |
|------|--------------|-----------------|
| **Very High** | Identical content — true duplicates | Auto-deduplicate |
| **High** | Same template, minor field differences | Review optional |
| **Medium** | Significant similarity, notable differences | Human review recommended |
| **None** | Unique document | No action needed |

### Business Rules

The platform includes a configurable rules engine that applies domain-specific logic on top of similarity analysis:

- **Version priority** — automatically identifies the authoritative version (FINAL over DRAFT)
- **Document type separation** — prevents grouping across document types (a permit is never grouped with an invoice)
- **Source separation** — keeps documents from different sources or jurisdictions separate
- **Redaction handling** — treats redacted and unredacted versions appropriately
- **Date proximity** — considers document dates when evaluating matches

Rules are fully configurable through the web interface without code changes.

---

## Oil & Gas Applications

### Regulatory Compliance

**Well Permits & Applications:** Identify duplicate permit filings across state agencies. Match amended permits to their originals. Ensure the current, approved version is the one on record.

**Environmental Reports:** Link compliance reports across reporting periods. Detect when the same environmental assessment has been filed under different well names or API numbers.

**Inspection Records:** Group inspection reports by well site and date. Identify when the same inspection has been documented multiple times through different channels.

### Land & Title Management

**Surface Use Agreements:** Match agreements across operators, landowners, and county records. Identify when the same agreement exists in multiple filing systems with different Bates numbers.

**Mineral Leases:** Detect duplicate lease filings. Match amended leases to original agreements. Identify lease assignments that reference the same underlying instrument.

**Title Opinions & Abstracts:** Group related title documents. Identify when the same title opinion has been updated or superseded.

### Legal Discovery & Litigation Support

**Document Production:** Before producing documents in litigation, identify and eliminate duplicates to reduce review costs. DocGrouping's confidence tiers map directly to production review workflows — Very High and High confidence duplicates can be bulk-deduplicated, while Medium confidence matches are flagged for attorney review.

**Privilege Review:** Group related documents so privilege determinations can be applied consistently across all versions of the same document.

### Asset Transactions

**Data Room Preparation:** When preparing a virtual data room for asset sales, DocGrouping identifies duplicate documents across thousands of files, reducing the data room to unique content and saving buyer review time.

**Due Diligence:** Buyers can use DocGrouping to compare their existing records against acquired assets, quickly identifying which documents they already have and which are new.

---

## Visual Document Comparison

When documents are grouped, the platform provides rich comparison tools:

**Side-by-Side Text Diff** — Word-level highlighting shows exactly what changed between two documents. Added text is highlighted in green, removed text in red.

**PDF Visual Comparison** — Original PDFs are rendered side-by-side with pixel-level difference highlighting. This catches formatting changes, signature differences, and visual elements that text comparison alone would miss.

**Similarity Metrics** — Three complementary metrics provide a complete picture:
- *Jaccard Similarity* — what fraction of vocabulary is shared
- *Overlap Coefficient* — how much of the smaller document appears in the larger one
- *Fuzzy Signature Match* — how similar are the documents' most characteristic words

---

## Scale & Performance

DocGrouping is designed for large-scale document collections:

| Metric | Capability |
|--------|-----------|
| **Tested volume** | 800,000 documents processed and grouped |
| **Target volume** | Millions of documents across terabytes of storage |
| **Grouping speed** | Sub-minute for 800K documents |
| **Comparison efficiency** | Avoids brute-force N^2 comparison through locality-sensitive hashing |
| **Incremental processing** | New documents matched against existing groups without reprocessing |

The system uses **MinHash and Locality-Sensitive Hashing (LSH)** to reduce the comparison space. Instead of comparing every document to every other document (which would require billions of comparisons for a million documents), LSH identifies likely matches in linear time, then verifies only those candidates with exact similarity computation.

---

## Architecture Highlights

**Multi-Database Isolation** — Separate databases for different projects, clients, or testing environments. Switch between them from a single UI.

**Configurable Thresholds** — Similarity thresholds are adjustable through the Settings page. Tighten thresholds for higher precision (fewer false matches) or loosen them for higher recall (fewer missed matches). Changes take effect on the next grouping run.

**Canonical Document Management** — Designate authoritative reference documents. New documents are classified against these canonicals, making it easy to identify which reference document a new filing most closely matches.

**Complete Audit Trail** — Every grouping decision records the method used (exact hash, fuzzy hash, Jaccard similarity with score), the confidence tier, and which business rules were evaluated. This is critical for regulatory and legal defensibility.

**API-First Design** — Full REST API enables integration with existing document management systems, workflow tools, and automated pipelines. Every operation available in the UI is also available via API.

---

## Deployment Options

DocGrouping runs on standard infrastructure:

- **.NET 10** application — runs on Windows or Linux
- **PostgreSQL** database — supports managed services (AWS RDS, Azure Database for PostgreSQL)
- **File storage** — local disk or network storage for PDF files (S3-compatible storage for cloud deployments)
- **No GPU required** — all processing is CPU-based, using mathematical hashing rather than machine learning models

For cloud-scale deployments, the architecture supports:
- **AWS:** S3 for document storage, RDS PostgreSQL for metadata, Batch for processing
- **Azure:** Blob Storage, Azure Database for PostgreSQL, Container Instances
- **On-premises:** Standard server infrastructure with PostgreSQL

---

## Key Differentiators

**Content-Based, Not File-Based** — DocGrouping compares document *content* after normalization, not raw files. A scanned copy and a born-digital copy of the same document will match, even though their files are completely different.

**OCR-Aware** — The normalization pipeline includes systematic OCR error correction, handling the common character substitutions (`0`/`o`, `1`/`l`, `rn`/`m`) that cause scanned documents to appear different from their digital originals.

**Artifact-Tolerant** — Bates stamps, fax headers, page numbers, confidentiality footers, and date stamps are stripped before comparison. These are markings applied *to* documents, not part of the documents themselves.

**Mathematically Grounded** — The similarity metrics (Jaccard similarity, MinHash, LSH) are well-established in information retrieval with known error bounds and probabilistic guarantees. This isn't a black-box AI model — every decision is explainable and reproducible.

**Transparent Decisions** — Every grouping decision can be examined: what method was used, what score was computed, what rules were evaluated, and why the confidence tier was assigned. This transparency is essential for legal and regulatory contexts.

**Configurable Precision** — The balance between precision (avoiding false matches) and recall (catching all true matches) is configurable through threshold settings, allowing the system to be tuned for different use cases and risk tolerances.

---

## Summary

DocGrouping transforms document deduplication from a manual, error-prone process into an automated, auditable, and scalable operation. For oil and gas companies managing millions of documents across regulatory, land, legal, and operational functions, it provides:

- **Cost reduction** through automated duplicate identification
- **Risk mitigation** through authoritative version tracking
- **Efficiency gains** through elimination of redundant manual review
- **Legal defensibility** through transparent, explainable grouping decisions
- **Scalability** from hundreds to millions of documents with consistent performance
