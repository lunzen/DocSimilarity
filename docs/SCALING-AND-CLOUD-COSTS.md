# DocGrouping — Scaling & AWS Cloud Cost Analysis

*Revised March 2026 — fresh assessment based on current codebase and proven benchmarks*

---

## 1. What We Know Now vs. What We Assumed Before

The original scaling estimate (February 2025) was written before the application existed at scale. Since then:

| Factor | Original Assumption | Current Reality |
|--------|-------------------|-----------------|
| **Local benchmark** | None — theoretical estimates only | **800K documents grouped successfully** on a single developer machine |
| **Batch ingestion** | Not implemented | Implemented — batch sizes of 500 groups with change tracker clearing |
| **Incremental grouping** | Not implemented | Implemented — Phase 2 strict mode, new docs compared only against existing index |
| **Memory management** | Assumed OOM crash at scale | Proven at 800K with batch processing and tracker clearing, but still loads full doc set for hash indexing |
| **Text storage** | Assumed inline in DB | Still inline — not yet externalized, but PostgreSQL TOAST handles it at 800K |
| **Union-find** | Recommended but not implemented | Implemented in Phase 3 merge logic |
| **MinHash/LSH persistence** | Tables exist but not populated | Still computed in-memory per run — not persisted between runs |
| **Background jobs** | Not implemented | Partially — Generator page has progress tracking, but grouping still runs on request thread |

**Bottom line:** The application is much more capable than when the original estimate was written, but several critical gaps remain for production-scale cloud deployment.

---

## 2. Scenario Recap

| Parameter | Value |
|-----------|-------|
| Phase 1 (golden truth) | 13 TB of born-digital PDFs |
| Phase 2 (comparison set) | 8 TB of born-digital PDFs |
| Total storage | 21 TB |
| Operation | Compare Phase 2 against Phase 1 only |
| Document counts | Unknown — estimates below use ranges |

### Estimating Document Counts

This is the biggest unknown. The cost model depends heavily on document count, not just raw TB.

| Assumption | Avg PDF Size | Phase 1 Docs | Phase 2 Docs | Total |
|-----------|-------------|-------------|-------------|-------|
| Large complex docs (100+ pages) | 10 MB | ~1.3M | ~800K | ~2.1M |
| Medium docs (10-50 pages) | 2 MB | ~6.5M | ~4M | ~10.5M |
| Small docs (1-5 pages) | 500 KB | ~26M | ~16M | ~42M |
| Mixed (realistic for O&G) | ~3 MB | ~4.3M | ~2.7M | ~7M |

**Working estimate for costs below: ~7 million documents total.**

---

## 3. Revised Processing Pipeline

Based on what we've built and proven, here's the pipeline for cloud deployment:

```
STEP 1: TEXT EXTRACTION (parallelizable, one-time)
├── Pull PDFs from S3
├── Extract text via PdfPig (born-digital = fast, no OCR)
├── Write .txt to S3
├── Estimated output: 200 GB – 1 TB of text
└── This step is embarrassingly parallel — scale with workers

STEP 2: INGEST & FINGERPRINT (parallelizable, one-time)
├── Read .txt files
├── Normalize text (8-step pipeline)
├── Compute TextHash (SHA-256), FuzzyHash (top-50 signature hash)
├── Compute MinHash signature (100 universal hashes)
├── Write metadata to PostgreSQL (NOT full text — see §4.1)
└── Parallelizable per document

STEP 3: INDEX PHASE 1 (one-time, must complete before Step 4)
├── Build LSH buckets from MinHash signatures (20 bands × 5 rows)
├── SQL GROUP BY on TextHash and FuzzyHash for instant Phase 1/2 matching
├── Persist all index data to database
└── Result: Phase 1 is fully searchable

STEP 4: COMPARE EACH PHASE 2 FILE (parallelizable)
├── For each Phase 2 document:
│   ├── Layer 1: Exact hash lookup against Phase 1 → MATCH? Done.
│   ├── Layer 2: Fuzzy hash lookup against Phase 1 → MATCH? Done.
│   ├── Layer 3: LSH candidate query → Jaccard verify → MATCH? Done.
│   └── Layer 4: (Optional) Embedding similarity → MATCH? Done.
│   └── No match → flag as unmatched
├── Persist Phase 2 index data for future use
└── Each file is independent — scale with workers

STEP 5: OUTPUT
├── Group assignments with confidence tiers
├── Export as CSV/Parquet or via API
└── Dashboard for review
```

### What's Different from the Original Estimate

1. **No RAG/embedding layer in the initial run.** The hash + LSH + Jaccard cascade handles the overwhelming majority of matches for born-digital documents. Embeddings add cost and complexity for marginal gain on this document type. Reserve for a second pass on unmatched documents if the client wants semantic matching.

2. **Text externalization is now a clear requirement**, not an option. At 7M documents, storing `OriginalText` + `NormalizedText` inline would make the database 70-350 GB. Store text on S3, keep only hashes and metadata in PostgreSQL.

3. **Bucketing by document type is valuable but not required for correctness.** The hash cascade naturally handles type separation — a well permit won't hash-match an invoice. Bucketing is a performance optimization that reduces LSH candidate space.

---

## 4. Revised AWS Architecture

### 4.1 Infrastructure

| Component | AWS Service | Config | Purpose |
|-----------|------------|--------|---------|
| **PDF storage** | S3 Standard | 21 TB | Permanent home for all PDFs |
| **Extracted text** | S3 Standard | ~200 GB – 1 TB | Persisted .txt files, read during Jaccard verification |
| **Web application** | EC2 or ECS Fargate | t3.large (2 vCPU, 8 GB) | Blazor app for job management, results, comparison UI |
| **Database** | RDS PostgreSQL | db.r6g.xlarge (4 vCPU, 32 GB, 500 GB gp3) | Metadata, hashes, MinHash signatures, LSH buckets, groups |
| **Processing workers** | AWS Batch | c7i.4xlarge (16 vCPU, 32 GB) × N | Text extraction, fingerprinting, comparison |
| **Embeddings (optional)** | Amazon Bedrock | Titan Embeddings v2 | Only for unmatched docs if semantic matching requested |
| **Monitoring** | CloudWatch | Dashboards + Logs | Processing metrics, health checks |
| **CI/CD** | ECR + CodePipeline | — | Docker image builds and deployment |

### 4.2 Why the Instance Sizes Changed

The original estimate spec'd a single c7i.8xlarge (32 vCPU, 64 GB) worker. That's wrong for this workload:

**Multiple smaller workers are better than one large worker because:**
- Text extraction and Phase 2 comparison are embarrassingly parallel (each file is independent)
- 4 × c7i.4xlarge gives the same total compute as 1 × c7i.16xlarge but with fault isolation
- If one worker dies, 75% of progress is preserved
- AWS Batch handles scaling and scheduling automatically
- Spot instances can save 60-70% on compute costs

**The database needs more RAM than CPU:**
- At 7M documents, the MinHash signatures table alone is ~2.8 GB (7M × 400 bytes)
- LSH buckets: ~8.4 GB (7M × 20 bands × 60 bytes)
- Index data: another 3-5 GB
- Total working set: ~15-20 GB — must fit in PostgreSQL shared buffers for decent query performance
- db.r6g.xlarge (32 GB RAM) is the minimum; db.r6g.2xlarge (64 GB) is safer

### 4.3 Data Flow

```
┌─────────────────────────────────────────────────────────────────┐
│                         S3 Bucket                               │
│  /phase1-pdfs/          (13 TB)                                 │
│  /phase2-pdfs/          (8 TB)                                  │
│  /extracted-text/       (200 GB – 1 TB)                         │
└──────────┬──────────────────────────────────────┬───────────────┘
           │                                      │
           ▼                                      ▼
┌─────────────────────────┐         ┌────────────────────────────┐
│   AWS Batch Workers     │         │    Blazor App (EC2/ECS)    │
│   (c7i.4xlarge × N)    │         │                            │
│                         │         │  • Job submission          │
│  Step 1: Pull PDF → S3  │         │  • Real-time progress      │
│  Step 2: Extract text   │         │  • Results browser         │
│  Step 3: Fingerprint    │         │  • PDF comparison UI       │
│  Step 4: Compare → DB   │         │  • Threshold configuration │
│  Push .txt back to S3   │         │                            │
│                         │         └──────────┬─────────────────┘
└──────────┬──────────────┘                    │
           │                                   │
           ▼                                   ▼
┌─────────────────────────────────────────────────────────────────┐
│                      RDS PostgreSQL                              │
│                                                                  │
│  Documents:        hashes, metadata, word counts  (NOT text)    │
│  MinHashSignatures: 100-int arrays per document                 │
│  LshBuckets:       20 band entries per document                 │
│  DocumentGroups:   groups with confidence tiers                 │
│  Memberships:      document-to-group assignments                │
│                                                                  │
│  Estimated size: 20-40 GB (text externalized to S3)             │
└─────────────────────────────────────────────────────────────────┘
```

---

## 5. Revised Cost Estimates

### 5.1 Processing Phase Costs (One-Time Run)

#### Step 1: Text Extraction

| Parameter | Estimate |
|-----------|----------|
| Documents to extract | ~7M |
| Avg extraction time per doc (born-digital) | ~0.5 seconds |
| Total CPU-seconds | ~3.5M seconds (~970 hours) |
| Workers (c7i.4xlarge, 16 vCPU each) | 8 workers |
| Wall-clock time | ~8-12 hours |
| On-demand cost per worker | $0.68/hr |
| **Total extraction compute cost** | **$44-$65** |
| With Spot instances (60% savings) | **$18-$26** |

#### Step 2: Ingest & Fingerprint

| Parameter | Estimate |
|-----------|----------|
| Documents to fingerprint | ~7M |
| Avg fingerprint time per doc | ~0.2 seconds (normalize + hash + MinHash) |
| Total CPU-seconds | ~1.4M seconds (~390 hours) |
| Workers | 8 workers |
| Wall-clock time | ~3-5 hours |
| **Total fingerprint compute cost** | **$16-$27** |
| With Spot instances | **$6-$11** |

#### Step 3: Index Phase 1

| Parameter | Estimate |
|-----------|----------|
| Phase 1 documents | ~4.3M |
| LSH bucket inserts | ~86M rows (4.3M × 20 bands) |
| Bulk insert rate | ~50K rows/sec (batched COPY) |
| Wall-clock time | ~30-60 minutes |
| **Compute cost** | **Included in worker time above** |
| **DB IOPS cost** | **Included in RDS provisioned IOPS** |

#### Step 4: Compare Phase 2

| Parameter | Estimate |
|-----------|----------|
| Phase 2 documents | ~2.7M |
| Expected Layer 1 (exact hash) matches | ~20-40% (common in O&G — same doc, different file paths) |
| Expected Layer 2 (fuzzy hash) matches | ~10-20% |
| Expected Layer 3 (LSH + Jaccard) candidates | ~5-15% need verification |
| Remaining unmatched | ~20-40% |
| Avg LSH candidates per unmatched doc | ~10-50 |
| Jaccard verification time per pair | ~1-5 ms (requires S3 text fetch for both docs) |
| S3 GET requests for text (worst case) | ~5M requests |
| Wall-clock time (8 workers) | ~4-8 hours |
| **Compute cost** | **$22-$44** |
| **S3 GET request cost** | **$2-$3** (5M × $0.0004/1000) |
| With Spot instances | **$9-$18 compute** |

#### Processing Phase Total

| Scenario | Compute | S3 Requests | Total |
|----------|---------|-------------|-------|
| **On-demand instances** | $82-$136 | $2-$3 | **$85-$140** |
| **Spot instances (recommended)** | $33-$55 | $2-$3 | **$35-$58** |

### 5.2 Infrastructure Costs (Monthly)

| Service | Configuration | Monthly Cost |
|---------|--------------|-------------|
| **S3 storage (21 TB PDFs)** | Standard tier | **$483** |
| **S3 storage (extracted text, ~500 GB)** | Standard tier | **$12** |
| **RDS PostgreSQL** | db.r6g.xlarge, 500 GB gp3, Multi-AZ | **$460** |
| **EC2 Blazor host** | t3.large, always on | **$60** |
| **CloudWatch** | Dashboards + custom metrics + logs | **$15-$30** |
| **ECR** | Container image storage | **$5** |
| **Data transfer** | Minimal egress | **$5-$10** |
| **Total (idle, between runs)** | | **$1,040-$1,060** |

### 5.3 Cost Optimization Strategies

| Strategy | Savings | Trade-off |
|----------|---------|-----------|
| **S3 Intelligent-Tiering for PDFs** | ~$100-$200/mo | No trade-off — auto-tiers to cheapest access pattern |
| **S3 Infrequent Access for Phase 1 PDFs** (read once during extraction, then rarely) | ~$150/mo | 30-day minimum storage, retrieval fee |
| **Stop RDS between runs** | ~$460/mo saved when idle | 15-20 min startup time, automated backups still charged |
| **Spot instances for Batch workers** | 60-70% compute savings | Workers can be interrupted (Batch handles retry) |
| **Reserved Instance for RDS** (1-year) | ~30% savings ($140/mo) | Commitment |
| **Graviton (ARM) instances** | ~20% cheaper than x86 | .NET 10 runs natively on ARM, no code changes |

### 5.4 Total Cost Summary

| Scenario | First Run | Monthly (Always On) | Monthly (Optimized) |
|----------|-----------|--------------------|--------------------|
| **Process 7M docs, keep infra running** | $35-$140 (compute) | $1,040-$1,060 | $600-$750 (with RI + S3 tiering + RDS stop) |
| **Process, then tear down non-storage** | $35-$140 | $495 (S3 only) | $350 (S3 IA for Phase 1) |
| **Recurring incremental runs** | — | +$35-$60 per run | Spot + S3 text cache = cheap |

### 5.5 Comparison with Original Estimate

| Item | Original Estimate | Revised Estimate | Why Different |
|------|------------------|------------------|---------------|
| Idle monthly cost | $790/mo | $1,040-$1,060/mo | More realistic RDS sizing (Multi-AZ), larger EC2 for Blazor |
| Processing compute | $12-$25 (single run) | $35-$140 | Accounts for text extraction step separately; more conservative |
| Processing time | 4-8 hours (Tier 1+2 changes) | 15-25 hours total | Includes extraction time; more realistic with S3 I/O |
| Bedrock embedding cost | "Depends on doc count" | **$0 for initial run** | Removed from initial pipeline — hash cascade sufficient for born-digital |
| RDS instance | db.r6g.xlarge ($250/mo) | db.r6g.xlarge Multi-AZ ($460/mo) | Production needs Multi-AZ for reliability |
| Workers | 1 × c7i.8xlarge | 8 × c7i.4xlarge | Parallel workers with fault isolation |

---

## 6. What Needs to Change in the Application

The original estimate listed Tier 1/2/3 changes. Here's what's been done and what remains:

### Already Implemented

| Change | Status | Notes |
|--------|--------|-------|
| Batch database writes | Done | 500-group batches with ChangeTracker.Clear() |
| Union-find merge | Done | Used in Phase 3 LSH pair merging |
| Incremental processing | Done | Phase 2 strict mode — new docs vs existing only |
| Projection queries | Done | Hash-only queries, GetCountAsync, pagination |
| Database indexes | Done | TextHash, FuzzyHash indexes in EF config |
| Multi-database support | Done | Switch between databases via UI or API header |

### Still Required for Cloud Scale

| Change | Priority | Effort | Description |
|--------|----------|--------|-------------|
| **Externalize text to S3** | Critical | Medium | Remove OriginalText/NormalizedText from Documents table. Store on S3, fetch on-demand for Jaccard. At 7M docs, inline text = 70-350 GB in PostgreSQL. |
| **Background job system** | Critical | Medium | Grouping runs on the HTTP request thread. At 7M docs, this will timeout. Need Hangfire or BackgroundService + ProcessingJobs table (already exists). |
| **Persist MinHash/LSH** | Critical | Low-Medium | Currently recomputed every run. At 7M Phase 1 docs, this is ~30 minutes of wasted compute on every re-run. Tables exist, write logic doesn't. |
| **Streaming document loading** | Critical | Medium | Phase 3 still loads ungrouped docs into memory for LSH. At 7M docs this is manageable for hash data only (~2.8 GB MinHash), but full entities would OOM. Need to ensure only projections are loaded. |
| **Staged pipeline with checkpointing** | High | High | If the process crashes at Phase 3 after 10 hours of extraction, everything restarts. Each stage should checkpoint to the database and be independently restartable. |
| **AWS Batch integration** | High | Medium | Package processing stages as Docker containers. AWS Batch handles scaling, scheduling, and retry. Each worker processes a partition of documents. |
| **Compiled regex** | Low | Low | TextNormalizer uses inline Regex.Replace ~15 times per doc. Use source-generated regex for 5-10% CPU savings. At 7M docs this saves ~1 hour. |
| **S3 text fetch caching** | Medium | Low | During Jaccard verification, workers fetch .txt files from S3. Add local NVMe caching to avoid re-fetching the same Phase 1 text file for multiple comparisons. |

### Application Changes NOT Needed

| Previously Listed | Why Not Needed |
|-------------------|---------------|
| RAG/embedding layer | Hash + LSH + Jaccard is sufficient for born-digital docs with identical source text. Embeddings catch "same meaning, different words" — but these are all born-digital copies of the same documents. Save for a future unmatched-document sweep. |
| pgvector extension | Not needed without embeddings. |
| OpenSearch Serverless | Not needed without embeddings. |
| Bedrock Knowledge Bases | Not needed without embeddings. |

**This is a significant cost simplification.** The original estimate included Bedrock token costs as an open-ended variable. By recognizing that born-digital documents with the same content will have identical or near-identical text (which hash/LSH catches reliably), we can defer the entire embedding infrastructure.

---

## 7. Processing Time Estimates (Revised)

| Phase | Work | Wall-Clock (8 workers) |
|-------|------|----------------------|
| Text extraction (both phases) | 7M PDFs → .txt | 8-12 hours |
| Ingest & fingerprint | Normalize, hash, MinHash for 7M docs | 3-5 hours |
| Index Phase 1 | LSH bucket build for 4.3M docs | 30-60 min |
| Phase 1/2 hash grouping | SQL GROUP BY on TextHash, FuzzyHash | < 5 min |
| Phase 2 LSH comparison | 2.7M docs against Phase 1 LSH index | 4-8 hours |
| Singleton assignment | Remaining ungrouped → singleton groups | < 5 min |
| **Total** | | **16-26 hours** |

### Compared to Original Estimate

| Scenario | Original | Revised |
|----------|----------|---------|
| No changes | "Will not complete (OOM crash)" | Still true at 7M with full entities |
| Tier 1 changes only | 40-100+ hours | N/A — we've gone beyond Tier 1 |
| Tier 1 + Tier 2 | 4-8 hours | 16-26 hours (more realistic with extraction + S3 I/O) |
| All tiers | 1-3 hours | 16-26 hours |

**Why the revised estimate is higher:** The original 4-8 hour estimate assumed documents were already extracted and fingerprinted, and didn't account for S3 network I/O during Jaccard verification. The 16-26 hour estimate includes the full end-to-end pipeline from raw PDFs to final groups.

If we exclude extraction (done once, reusable):
- **Comparison-only time: 4-9 hours** — close to the original estimate.

---

## 8. Risk Factors and Unknowns

### Document Count

The biggest risk to these estimates. If the average PDF is 500 KB instead of 3 MB, we're looking at 42M documents instead of 7M — a 6x increase in processing time and database size. **The first task before committing to infrastructure should be sampling 1,000 files to determine average size and page count.**

### Text Extraction Failures

Some PDFs may be corrupted, password-protected, or image-only (despite being "born-digital"). The pipeline needs graceful handling:
- Log failures, don't stop the batch
- Track extraction success rate
- Image-only PDFs need OCR (Textract or Tesseract), which is 10-100x slower

### S3 Throughput

At 7M documents, S3 GET requests during Jaccard verification could bottleneck. Mitigations:
- Batch text fetches by LSH bucket (related documents are likely fetched together)
- Cache Phase 1 text locally on worker NVMe
- Use S3 Transfer Acceleration if cross-region

### Database Size at Scale

With text externalized, the database should stay under 50 GB. But MinHash signatures (7M × 400 bytes = 2.8 GB) and LSH buckets (140M rows × 60 bytes = 8.4 GB) plus indexes could push to 30-40 GB. Monitor `pg_total_relation_size` after Phase 1 indexing.

### Concurrency

The current application has no locking around group number assignment or grouping operations. Two simultaneous grouping runs would produce corrupt data. For cloud deployment:
- Use the ProcessingJobs table as a distributed lock
- Or use AWS Batch job dependencies to enforce ordering

---

## 9. Recommended Approach

### Phase A: Validate Assumptions (1-2 days)

1. **Sample document sizes** — pull 1,000 random PDFs from each phase, compute average file size, page count, and extracted text size
2. **Validate extraction** — confirm PdfPig handles the actual file formats; identify any that need OCR
3. **Estimate document count** — refine all cost models with real numbers

### Phase B: Application Changes (1-2 weeks)

1. Externalize text storage (S3 paths instead of inline text)
2. Add background job system (Hangfire or BackgroundService)
3. Persist MinHash signatures and LSH buckets
4. Add checkpointing to each pipeline stage
5. Ensure all phase queries use projections (no full entity loads)

### Phase C: Cloud Infrastructure (1 week)

1. AWS CDK stack: S3 bucket, RDS PostgreSQL, ECR, Batch compute environment
2. Docker images for processing workers
3. CloudWatch dashboards for processing metrics
4. CI/CD pipeline from GitHub

### Phase D: Pilot Run (1-2 days)

1. Extract and process 100K documents from each phase
2. Validate grouping quality
3. Measure actual throughput and adjust worker count
4. Tune thresholds with client feedback

### Phase E: Full Run (2-3 days)

1. Extract all text (8-12 hours)
2. Index Phase 1 (3-6 hours)
3. Compare Phase 2 (4-8 hours)
4. Generate deliverables
5. Client review

---

## 10. Cost Comparison: Build vs. Buy

For context, here's how DocGrouping compares to commercial document deduplication:

| Option | First-Year Cost | Ongoing Annual | Notes |
|--------|----------------|---------------|-------|
| **DocGrouping on AWS** | $15-$20K | $8-$13K | Infrastructure + development time |
| **Relativity Analytics** | $100K+ | $50K+/yr | Enterprise e-discovery platform, license per GB |
| **Nuix** | $75K+ | $40K+/yr | License-based, per-processing-volume |
| **Custom ML pipeline** | $50-$100K | $20-$40K | Data science team + GPU infrastructure |
| **Manual review** | $500K+ | Ongoing | At 7M documents × 30 seconds each = ~58,000 person-hours |

DocGrouping's approach — deterministic hashing + mathematical similarity rather than ML models — means:
- No GPU costs
- No model training or fine-tuning
- Reproducible, explainable results
- Dramatically lower infrastructure costs
