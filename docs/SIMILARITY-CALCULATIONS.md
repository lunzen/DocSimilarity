# Document Similarity & Grouping — How It Works

This document explains how DocGrouping determines whether two documents are "the same" or "similar," how confidence tiers are assigned, and what each metric means. Intended for internal teams and client-facing discussions.

---

## Table of Contents

1. [Overview: The Grouping Pipeline](#1-overview-the-grouping-pipeline)
2. [Text Preprocessing](#2-text-preprocessing)
3. [Fingerprinting: Three Layers of Identity](#3-fingerprinting-three-layers-of-identity)
4. [Similarity Metrics Explained](#4-similarity-metrics-explained)
5. [Confidence Tiers & Thresholds](#5-confidence-tiers--thresholds)
6. [The Four-Phase Grouping Process](#6-the-four-phase-grouping-process)
7. [Scaling: MinHash & LSH](#7-scaling-minhash--lsh)
8. [Configuring Thresholds](#8-configuring-thresholds)
9. [Talk Track: Common Questions](#9-talk-track-common-questions)

---

## 1. Overview: The Grouping Pipeline

DocGrouping processes documents through a multi-phase pipeline that moves from cheap/exact methods to expensive/approximate methods:

```
Raw PDF/Text
     |
     v
Text Extraction (PDF.js / iText)
     |
     v
Text Normalization (lowercase, OCR correction, artifact removal)
     |
     v
Fingerprinting (text hash, fuzzy hash, MinHash signature)
     |
     v
Four-Phase Grouping (exact hash -> fuzzy hash -> similarity -> singletons)
     |
     v
Confidence-Tiered Groups (VeryHigh, High, Medium, None)
```

The key insight: we never compare raw files. We compare **normalized text**, which strips away formatting differences, OCR artifacts, date stamps, and other noise that would cause byte-identical documents to appear different.

---

## 2. Text Preprocessing

Before any comparison, every document goes through a normalization pipeline:

| Step | What It Does | Why |
|------|-------------|-----|
| Unicode normalization | Converts to NFKC form; removes replacement characters | Handles ligatures (`ﬁ` -> `fi`), fullwidth chars |
| Lowercasing | `text.ToLowerInvariant()` | Case shouldn't affect similarity |
| OCR error correction | `0` in word context -> `o`, `1` -> `l`, `rn` -> `m` | Scanned documents have systematic OCR errors |
| Page number removal | Strips `Page X of Y`, standalone numbers | Page numbers differ across printings |
| Artifact removal | Strips Bates stamps, date stamps, fax headers, confidentiality footers | These are applied *to* documents, not *part of* them |
| Whitespace normalization | Collapses all whitespace to single spaces | Layout differences shouldn't affect matching |
| Punctuation normalization | Smart quotes -> ASCII, dashes normalized, decorative punctuation removed | Encoding/format differences |
| Hyphenation handling | Rejoins end-of-line hyphenated words | OCR and PDF extraction break words at line ends |

**Why this matters for clients:** Two copies of the same letter — one scanned, one born-digital — will have different raw text due to OCR errors, different page headers, and formatting artifacts. Normalization strips all of that away so the *content* can be compared fairly.

---

## 3. Fingerprinting: Three Layers of Identity

After normalization, each document gets three fingerprints, from most specific to most general:

### 3.1 Text Hash (SHA-256 of normalized text)
- **What:** A cryptographic hash of the entire normalized text
- **Guarantees:** If two documents have the same text hash, their normalized text is **byte-for-byte identical**
- **Use case:** Detecting exact duplicates (same document, different filenames or metadata)
- **Confidence tier:** VeryHigh

### 3.2 Fuzzy Hash (SHA-256 of top-K token signature)
- **What:** Extract the top 50 most frequent "meaningful" words (no stopwords, no short words, no numbers), sort alphabetically, hash that list
- **Guarantees:** Two documents with the same fuzzy hash share the same high-frequency vocabulary. Minor wording changes won't break the match
- **Use case:** Near-duplicates — same template/form with a few fields changed (names, dates, numbers)
- **Confidence tier:** High
- **Filtering rules for "meaningful" words:**
  - Minimum 6 characters
  - Not a stopword (a, the, is, was, etc.)
  - No digits
  - No currency symbols

### 3.3 MinHash Signature (100-dimensional locality-sensitive hash)
- **What:** A compact numeric signature that preserves set similarity. Uses 100 independent hash functions, each recording the minimum hash value across all tokens
- **Guarantees:** The fraction of matching positions between two signatures approximates the Jaccard similarity of their token sets
- **Use case:** Fast pre-filtering to find candidate pairs before expensive exact comparison
- **Not a confidence tier itself** — used to generate candidates for Phase 3

---

## 4. Similarity Metrics Explained

When two documents are compared at the similarity level, three metrics are computed:

### 4.1 Jaccard Similarity (Primary Metric)

```
Jaccard = |A ∩ B| / |A ∪ B|
```

- **A** = set of unique words in document 1
- **B** = set of unique words in document 2
- **|A ∩ B|** = words that appear in both
- **|A ∪ B|** = words that appear in either

**Range:** 0% (no words in common) to 100% (identical word sets)

**Intuition:** "What fraction of all vocabulary used across both documents is shared?"

**Strengths:**
- Symmetric — comparing A to B gives the same score as B to A
- Well-understood mathematically, with known probabilistic guarantees
- Size-fair — a 100-word document compared to a 10,000-word document will naturally score lower

**Limitations:**
- Penalizes size differences. If Doc A is a 2-page summary and Doc B is the same content plus 50 extra pages, Jaccard may be low even though A is fully contained in B
- Treats all words equally — "the" and "uranium" contribute equally
- Word frequency is ignored — a word appearing once and a word appearing 100 times are treated the same

**This is the metric that drives all grouping decisions for similarity-based tiers.**

### 4.2 Overlap Coefficient

```
Overlap = |A ∩ B| / min(|A|, |B|)
```

**Range:** 0% to 100%

**Intuition:** "What fraction of the *smaller* document's vocabulary appears in the larger document?"

**Strengths:**
- Handles size asymmetry well. If Doc A is a subset of Doc B, Overlap = 100% regardless of how much extra content Doc B has
- Good for detecting when one document is derived from another (summary vs. full report, excerpt vs. complete filing)

**Current use:** Displayed in the comparison UI for reference, but does not influence grouping decisions.

**Potential future use:** Could be combined with Jaccard in a composite rule, e.g., "If Jaccard < 0.70 but Overlap > 0.90, classify as Medium instead of None."

### 4.3 Fuzzy Signature Jaccard

```
FuzzyJaccard = |Sig(A) ∩ Sig(B)| / |Sig(A) ∪ Sig(B)|
```

Where `Sig(X)` is the set of top-50 frequent meaningful words.

**Range:** 0% to 100%

**Intuition:** "How similar are the documents' most characteristic vocabulary words?"

**Strengths:**
- Focuses on the words that define the document's *topic*, ignoring rare/unique terms
- Less sensitive to minor edits than full Jaccard
- If this is 100%, the documents almost certainly discuss the same subject matter

**Current use:** Displayed in the comparison UI. Also used indirectly — the fuzzy hash is a hash of this signature, so identical fuzzy signatures produce identical fuzzy hashes (High tier).

---

## 5. Confidence Tiers & Thresholds

Documents are grouped into confidence tiers based on how they were matched:

| Tier | Method | Default Jaccard Range | What It Means |
|------|--------|----------------------|---------------|
| **VeryHigh** | Exact text hash | = 100% | Normalized text is identical. These are the same document (different filename/metadata only) |
| **High** | Fuzzy hash match | Assumed ~90% | Same structural vocabulary. Likely the same form/template with field-level differences (names, dates, IDs) |
| **High** | Jaccard similarity | >= 85% (configurable) | Very similar content. Small section additions/removals or moderate field changes |
| **Medium** | Jaccard similarity | 70%–85% (configurable) | Moderate similarity. Same type of document with notable content differences. Requires human review |
| **None** | No match | < 70% (configurable) | No strong match found. Document is unique or sufficiently different from all others |

### Threshold Defaults

| Parameter | Default | Description |
|-----------|---------|-------------|
| `MediumMinJaccard` | 0.70 | Floor — below this, documents don't group |
| `HighMinJaccard` | 0.85 | Above this Jaccard = High; below = Medium |
| `FuzzyHashAssumedSimilarity` | 0.90 | Score recorded for fuzzy hash matches |
| `MinHashPrefilterThreshold` | 0.50 | LSH candidate pre-filter (performance tuning) |

These are configurable via the Settings page in the UI or `appsettings.json`.

---

## 6. The Four-Phase Grouping Process

### Phase 1: Exact Text Hash (VeryHigh Confidence)
1. Group all documents by their text hash
2. Documents sharing a text hash are byte-identical after normalization
3. **Cost:** O(1) per document (hash table lookup)
4. **Typical yield:** Catches all true exact duplicates

### Phase 2: Fuzzy Hash (High Confidence)
1. Among ungrouped documents, group by fuzzy hash
2. Documents sharing a fuzzy hash have the same top-50 vocabulary signature
3. **Cost:** O(1) per document (hash table lookup)
4. **Typical yield:** Catches near-duplicates (same form, different field values)

### Phase 3: Similarity Comparison (Medium/High Confidence)
1. Among remaining ungrouped documents, find pairs with Jaccard >= MediumMinJaccard
2. **For small sets (<200 docs):** Brute-force all pairs
3. **For large sets (200+ docs):** Uses MinHash + Locality-Sensitive Hashing (LSH) to generate candidate pairs, then verifies each with exact Jaccard
4. Pairs above HighMinJaccard become High; between MediumMinJaccard and HighMinJaccard become Medium
5. **Cost:** O(n) per document via LSH, vs O(n^2) brute-force

### Phase 4: Singletons (None Confidence)
1. All remaining documents that didn't match anything become singleton groups
2. Each gets a group of its own with confidence = None
3. **This is important:** Every document belongs to exactly one group. "None" doesn't mean ignored — it means "confirmed unique"

---

## 7. Scaling: MinHash & LSH

For large document collections (thousands to millions), comparing every pair is infeasible. MinHash and LSH make this tractable:

### MinHash (Compact Similarity Fingerprint)
- Each document gets a 100-integer signature
- The probability that two signatures match at any position equals the Jaccard similarity of the underlying word sets
- **Key property:** We can estimate Jaccard similarity from signatures alone in O(100) time, without loading full document text

### LSH — Locality-Sensitive Hashing (Candidate Generation)
- Splits each 100-value MinHash signature into 20 bands of 5 values each
- Two documents become candidates if they match in *any* band
- **Detection probability at different Jaccard levels:**
  - J=0.70: ~97.5% chance of becoming candidates
  - J=0.50: ~56% chance
  - J=0.30: ~4.7% chance
- This reduces comparisons from O(n^2) to O(n * average candidates per doc)

### Pre-filter
- Before loading full text for exact Jaccard, we estimate Jaccard from MinHash signatures
- Candidates below `MinHashPrefilterThreshold` (default 0.50) are skipped
- This avoids expensive text loading for pairs that are clearly below the grouping threshold

---

## 8. Configuring Thresholds

### When to Adjust

**Lower the Medium Min (e.g., 0.60):**
- More document pairs will be grouped
- Useful when documents undergo significant edits but should still be associated
- Risk: more false positives (documents grouped that shouldn't be)
- Also lower the MinHash Pre-filter accordingly (e.g., to 0.40)

**Raise the Medium Min (e.g., 0.80):**
- Fewer pairs grouped, only highly similar documents
- Useful when precision is more important than recall
- Risk: missing legitimate near-duplicates

**Lower the High Min (e.g., 0.75):**
- More groups promoted from Medium to High
- Useful when fuzzy differences (field changes) are expected and accepted
- More groups won't need manual review

**Raise the High Min (e.g., 0.95):**
- Only near-identical documents get High confidence
- Useful when High confidence should mean "essentially the same document"
- More groups will fall into Medium, requiring review

### Impact on Existing Groups
Changing thresholds does **not** retroactively reclassify existing groups. To apply new thresholds, re-run the grouping process.

---

## 9. Talk Track: Common Questions

### "How accurate is this?"
The system uses a cascading approach — starting with mathematically guaranteed exact matches (hash comparison), then moving to well-understood statistical methods (Jaccard similarity). The VeryHigh and High tiers from hash matching have effectively zero false positive rate. The similarity-based tiers (Medium, High-via-Jaccard) depend on the threshold settings, which are configurable to match your tolerance for false positives vs. false negatives.

### "Why not just compare the files directly?"
Raw files differ for many reasons that don't reflect content differences: different PDF generators, OCR vs. born-digital, different page layouts, Bates stamps added after production, confidentiality footers. By normalizing text first, we compare *content*, not *formatting*.

### "What about scanned documents?"
Scanned documents go through OCR before entering the pipeline. The normalizer includes OCR error correction (common substitutions like `0`/`o`, `1`/`l`, `rn`/`m`). This improves matching between a scanned copy and a born-digital copy of the same document. However, very poor OCR quality will reduce similarity scores — this is by design, as the extracted text genuinely differs.

### "Can two documents be in the same group if they're clearly different?"
The Medium tier (default 70-85% Jaccard) is specifically designed as a "review needed" zone. These documents share significant content but have notable differences. The system surfaces them for human review rather than declaring them duplicates. You can raise the Medium minimum to be more conservative, or lower it to catch more borderline cases.

### "How does this handle large volumes?"
The system uses MinHash and Locality-Sensitive Hashing to avoid comparing every document to every other document. For N documents, naive comparison requires N*(N-1)/2 comparisons. LSH reduces this to approximately N * k comparisons, where k is the average number of candidates per document (typically much less than N). In practice, 800,000 documents were grouped in under a minute.

### "What if we want stricter or looser matching?"
The Settings page exposes four threshold sliders with live preview of how the tier boundaries change. Lower the Medium minimum to catch more matches (at the risk of false positives), or raise it to be more selective. Changes apply on the next grouping run — existing groups are preserved.

### "Why is Jaccard the primary metric and not something else?"
Jaccard similarity is the standard metric for set comparison because:
1. It's symmetric (A vs B = B vs A)
2. It has a clear probabilistic interpretation ("fraction of shared vocabulary")
3. It can be efficiently approximated with MinHash for large-scale processing
4. It's well-studied with known error bounds

The system also computes Overlap Coefficient (better for size-asymmetric comparisons) and Fuzzy Signature Jaccard (focused on characteristic vocabulary). These are displayed for analysis but not currently used for tier assignment. They could be incorporated into composite rules in the future if needed.

### "What's the difference between text hash and fuzzy hash?"
**Text hash:** If two documents have the same text hash, they are *identical* after normalization. Not a single word differs. This catches true duplicates that were just filed under different names.

**Fuzzy hash:** If two documents have the same fuzzy hash, they share the same 50 most frequent meaningful words. Individual field values (names, dates, dollar amounts, ID numbers) can differ freely because those tend to be low-frequency words or contain digits (which are filtered out). This is why fuzzy hash excels at matching form-based documents like regulatory filings, invoices, and reports — the template vocabulary dominates the signature.

### "Can we see why two specific documents were grouped together?"
Yes. The Comparison page shows:
- Side-by-side text diff with word-level highlighting
- PDF rendering with visual diff overlay
- All three similarity metrics (Jaccard, Overlap, Fuzzy Signature)
- The match reason recorded during grouping (e.g., "Exact text match", "Fuzzy content match", "Jaccard similarity 82.3%")
- Metadata comparison (file sizes, hashes, word counts)

### "What about document types or metadata-based matching?"
The Rules Engine allows defining business rules that can prevent or modify grouping decisions based on metadata (document type, source folder, file naming patterns). For example: "Never group documents from different jurisdictions" or "Only group invoices with other invoices." These rules are evaluated after similarity is computed and can override the similarity-based decision.
