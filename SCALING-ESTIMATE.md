# DocGrouping — Scaling Estimate for Large-Scale Document Processing

## Scenario

| Parameter                         | Value                                                                          |
| --------------------------------- | ------------------------------------------------------------------------------ |
| Phase 1 collection (golden truth) | 13 TB                                                                          |
| Phase 2 collection                | 8 TB                                                                           |
| Total file storage                | 21 TB                                                                          |
| File type                         | All digital (born-digital, no scanning/OCR required)                           |
| Operation                         | Compare Phase 2 docs against Phase 1 (golden truth) for grouping/deduplication |

> **Note:** Individual document counts and page counts are not yet known. Only total file sizes are confirmed. Estimates below that depend on document count are expressed as formulas or ranges and should be revised once counts are available.

---

## 0. Recommended Pre-Processing Step: Extract Text to Files

Before any comparison work begins, extract the text content from all PDFs into plain text files. All subsequent processing (normalization, hashing, MinHash, LSH, comparison) operates on extracted text — not the raw PDFs — so this step should be done once up front.

**Why this matters at 21 TB scale:**

- **Massive size reduction:** 21 TB of born-digital PDFs will yield an estimated **200 GB – 1 TB of text files** (text is typically 1–5% of PDF file size). This is small enough to fit on local fast SSD, eliminating network I/O during comparison.
- **Extract once, never re-parse:** If comparison parameters need tuning or a run fails partway through, you re-run from the text files — no need to touch the 21 TB of PDFs again.
- **Simpler comparison pipeline:** The comparison phase has no PDF library dependency. It reads plain text files, which are fast to open, trivial to debug (`grep`, spot-check), and easy to stream.
- **Easy parallelization:** Text extraction can fan out across many workers writing to a shared folder. Each file is independent.

**Approach:**

1. For each PDF, extract all text content and write to a `.txt` file with the same name (preserving the folder structure or a flat layout with unique names)
2. Store the text files on fast local or network storage — they're small enough
3. Maintain a simple mapping (filename or database record) from each text file back to its source PDF
4. All downstream steps (bucketing, indexing, comparison) read from the text files, never the PDFs

**Storage estimate:**

| Item                     | Volume             | Notes             |
| ------------------------ | ------------------ | ----------------- |
| Phase 1 extracted text   | ~130 GB – 650 GB   | 1–5% of 13 TB     |
| Phase 2 extracted text   | ~80 GB – 400 GB    | 1–5% of 8 TB      |
| **Total extracted text** | **~200 GB – 1 TB** | Fits on local SSD |

This pre-processing step aligns with Stage 1 of the staged pipeline architecture described in §2.3.1, but can be done independently as a simple first step before any application changes are needed.

---

## 0.1 End-to-End Process Flow

```
╔═══════════════════════════════════════════════════════════════════════════╗
║                     STEP 1: TEXT EXTRACTION                              ║
║                     (one-time, both phases)                              ║
╚═══════════════════════════════╤═══════════════════════════════════════════╝
                                │
          ┌─────────────────────┴─────────────────────┐
          ▼                                           ▼
  ┌───────────────────┐                   ┌───────────────────┐
  │  Phase 1 PDFs     │                   │  Phase 2 PDFs     │
  │  (13 TB on S3)    │                   │  (8 TB on S3)     │
  └────────┬──────────┘                   └────────┬──────────┘
           │                                       │
           ▼                                       ▼
  ┌───────────────────┐                   ┌───────────────────┐
  │  Extract text     │                   │  Extract text     │
  │  (AWS Batch)      │                   │  (AWS Batch)      │
  └────────┬──────────┘                   └────────┬──────────┘
           │                                       │
           ▼                                       ▼
  ┌───────────────────┐                   ┌───────────────────┐
  │  .txt files       │                   │  .txt files       │
  │  (~130–650 GB)    │                   │  (~80–400 GB)     │
  │  persisted to S3  │                   │  persisted to S3  │
  └────────┬──────────┘                   └────────┬──────────┘
           │                                       │
           └─────────────────┬─────────────────────┘
                             ▼
╔═══════════════════════════════════════════════════════════════════════════╗
║                     STEP 2: BUCKETIZE BY METADATA                        ║
║                     (document type, etc. — both phases)                   ║
╚═══════════════════════════════╤═══════════════════════════════════════════╝
                                │
                                ▼
                 ┌──────────────────────────────┐
                 │  Classify every file into    │
                 │  metadata buckets            │
                 │  (e.g., by document type)    │
                 └──────────────┬───────────────┘
                                │
            ┌───────────┬───────┴───────┬───────────┐
            ▼           ▼               ▼           ▼
       ┌─────────┐ ┌─────────┐    ┌─────────┐ ┌─────────┐
       │Bucket A │ │Bucket B │    │Bucket C │ │Bucket N │
       │(e.g.,   │ │(e.g.,   │    │(e.g.,   │ │  ...    │
       │ Birth   │ │ Death   │    │ Marriage│ │         │
       │ Certs)  │ │ Certs)  │    │ Certs)  │ │         │
       └────┬────┘ └────┬────┘    └────┬────┘ └────┬────┘
            │           │              │           │
            └─────┬─────┴──────┬───────┴─────┬─────┘
                  │            │             │
                  ▼            ▼             ▼
╔═══════════════════════════════════════════════════════════════════════════╗
║            STEP 3: INDEX PHASE 1 (golden truth)                          ║
║            (one-time per bucket, must complete before Step 4)             ║
╚═══════════════════════════════╤═══════════════════════════════════════════╝
                                │
                                ▼
              ┌─────────────────────────────────────┐
              │  For each Phase 1 file in bucket:   │
              │                                     │
              │  1. Normalize text                  │
              │  2. Compute exact hash (SHA-256)    │
              │  3. Compute fuzzy hash (SimHash)    │
              │  4. Compute MinHash signature       │
              │  5. Build LSH bucket entries        │
              │  6. Generate embeddings (Bedrock)   │
              │  7. Store all in RDS + pgvector     │
              └─────────────────┬───────────────────┘
                                │
                                ▼
              ┌─────────────────────────────────────┐
              │         PHASE 1 INDEX READY         │
              │  (per bucket, persisted in RDS)     │
              │                                     │
              │  • Exact hashes                     │
              │  • Fuzzy hashes                     │
              │  • MinHash signatures + LSH buckets │
              │  • Vector embeddings (pgvector)     │
              └─────────────────┬───────────────────┘
                                │
                                ▼
╔═══════════════════════════════════════════════════════════════════════════╗
║            STEP 4: COMPARE EACH PHASE 2 FILE                             ║
║            (file-by-file, ONLY against Phase 1 golden truth in bucket)   ║
║                                                                           ║
║   Phase 2 files are NEVER compared to other Phase 2 files.               ║
║   The only question: "Does this file have a match in Phase 1?"           ║
╚═══════════════════════════════╤═══════════════════════════════════════════╝
                                │
                                ▼
              ┌─────────────────────────────────────┐
              │  For each Phase 2 file:             │
              │                                     │
              │  1. Determine its metadata bucket   │
              │  2. Normalize text, compute hashes  │
              │  3. Persist index data to RDS       │
              │     (future-proofing — see §2.3.4.1)│
              └─────────────────┬───────────────────┘
                                │
                                ▼
              ┌─────────────────────────────────────┐
              │  LAYER 1: Exact Hash Match          │
              │  Compare SHA-256 against Phase 1    │
              │  hashes in this bucket              │
              └──────────┬──────────────────────────┘
                         │
                ┌────────┴────────┐
                ▼                 ▼
          ┌──────────┐     ┌──────────┐
          │  MATCH   │     │ NO MATCH │
          │  ✓ Done  │     │  ↓ Next  │
          │ (identical│     │  layer   │
          │  document)│     │          │
          └──────────┘     └────┬─────┘
                                │
                                ▼
              ┌─────────────────────────────────────┐
              │  LAYER 2: Fuzzy Hash Match          │
              │  Compare SimHash against Phase 1    │
              │  fuzzy hashes in this bucket         │
              └──────────┬──────────────────────────┘
                         │
                ┌────────┴────────┐
                ▼                 ▼
          ┌──────────┐     ┌──────────┐
          │  MATCH   │     │ NO MATCH │
          │  ✓ Done  │     │  ↓ Next  │
          │(near-dup)│     │  layer   │
          └──────────┘     └────┬─────┘
                                │
                                ▼
              ┌─────────────────────────────────────┐
              │  LAYER 3: LSH + Jaccard Verify      │
              │  Query LSH buckets for candidates,  │
              │  verify with Jaccard similarity      │
              └──────────┬──────────────────────────┘
                         │
                ┌────────┴────────┐
                ▼                 ▼
          ┌──────────┐     ┌──────────┐
          │  MATCH   │     │ NO MATCH │
          │  ✓ Done  │     │  ↓ Next  │
          │(textually│     │  layer   │
          │ similar) │     │          │
          └──────────┘     └────┬─────┘
                                │
                                ▼
              ┌─────────────────────────────────────┐
              │  LAYER 4: RAG Vector Search         │
              │  Generate embedding (Bedrock),      │
              │  query pgvector for semantic matches │
              └──────────┬──────────────────────────┘
                         │
                ┌────────┴────────┐
                ▼                 ▼
          ┌──────────┐     ┌──────────────┐
          │  MATCH   │     │  NO MATCH    │
          │  ✓ Done  │     │  ✓ Flag as   │
          │(semantic │     │  unmatched   │
          │ similar) │     │              │
          └──────────┘     └──────────────┘
                                │
                                ▼
╔═══════════════════════════════════════════════════════════════════════════╗
║                     STEP 5: RESULTS                                      ║
╚═══════════════════════════════╤═══════════════════════════════════════════╝
                                │
                                ▼
              ┌─────────────────────────────────────┐
              │  Final Output (per Phase 2 file):   │
              │                                     │
              │  • Matched group + confidence level  │
              │    (exact / fuzzy / LSH / semantic) │
              │  • Match score (Jaccard or cosine)  │
              │  • Matched Phase 1 document(s)      │
              │  — OR —                             │
              │  • Flagged as unmatched (new/unique)│
              └─────────────────────────────────────┘
                                │
                                ▼
              ┌─────────────────────────────────────┐
              │  Blazor Dashboard displays:         │
              │                                     │
              │  • Real-time progress (SignalR)     │
              │  • Match/unmatched counts           │
              │  • Confidence distribution          │
              │  • Per-file drill-down              │
              │  • Performance metrics              │
              └─────────────────────────────────────┘
                                │
                                ▼
              ┌─────────────────────────────────────┐
              │  PERSISTED PHASE 2 INDEX            │
              │  (future-proofing — see §2.3.4.1)  │
              │                                     │
              │  • All Phase 2 hashes, MinHash      │
              │    signatures, LSH buckets, and     │
              │    embeddings saved to RDS          │
              │  • NOT queried during golden-truth  │
              │    comparison                       │
              │  • Ready for Phase 2-to-Phase 2     │
              │    second pass if ever requested    │
              └─────────────────────────────────────┘
```

> **Key principle:** Each Phase 2 file cascades through the cheapest/fastest matching layer first. The moment a match is found, processing stops for that file. Only the hardest cases (no hash or text match) reach the RAG embedding layer, minimizing Bedrock API costs.

---

## 1. Hardware Estimates

### 1.1 Compute

Processing is CPU-bound across all phases (text extraction, normalization, hashing, MinHash, LSH bucketing, Jaccard verification). Since all files are born-digital, text extraction is fast direct-read (no OCR required), significantly reducing text-extraction CPU cost.

| Component | Minimum   | Recommended | Notes                                                                                                                                               |
| --------- | --------- | ----------- | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| CPU cores | 16        | 32–64       | MinHash and Jaccard verification parallelize well across cores                                                                                      |
| RAM       | 64 GB     | 128–256 GB  | LSH signatures scale as N × 100 ints × 4 bytes (e.g., 1M docs ≈ 400 MB); working sets for text normalization and pair verification require headroom |
| Local SSD | 1 TB NVMe | 2 TB NVMe   | Holds extracted text files (~200 GB – 1 TB per §0) plus scratch space, OS swap, logs. Source PDFs stay on network/blob storage (see §1.3)           |

**CPU clock speed guidance:**

Clock speed matters more than core count for single-threaded portions (text normalization, regex, Jaccard verification per pair), while core count matters for parallelizable phases (text extraction, MinHash computation).

| Factor       | Minimum | Recommended                          | Notes                                                                                  |
| ------------ | ------- | ------------------------------------ | -------------------------------------------------------------------------------------- |
| Base clock   | 2.5 GHz | 3.0+ GHz                             | Higher clock helps single-threaded regex/Jaccard work                                  |
| Turbo/boost  | 3.0 GHz | 3.5+ GHz                             | Burst speed for per-document processing                                                |
| Architecture | x86-64  | Modern (Zen 4, Sapphire Rapids, etc) | Newer architectures have better IPC — a 3.0 GHz Zen 4 outperforms a 3.5 GHz older Xeon |

> Compute-optimized instances (AWS c-family, Azure F-series) are the best fit — they're selected for high sustained clock speeds. General-purpose instances (D-series, m-family) work too if using modern architectures.

**Cloud VM equivalents:**

| Provider | Instance Type | vCPUs | RAM    | Approx. Cost/hr |
| -------- | ------------- | ----- | ------ | --------------- |
| Azure    | D32as_v5      | 32    | 128 GB | ~$1.50          |
| Azure    | D64as_v5      | 64    | 256 GB | ~$3.00          |
| AWS      | c6i.8xlarge   | 32    | 64 GB  | ~$1.36          |
| AWS      | c6i.16xlarge  | 64    | 128 GB | ~$2.72          |
| AWS      | r6i.16xlarge  | 64    | 512 GB | ~$4.03          |

For a single processing run lasting ~4–8 hours (optimized architecture), compute cost would be approximately **$12–$25**.

For extended runs on current architecture (40–100+ hours, if it completes at all), compute cost would be **$60–$300**.

### 1.2 Database (PostgreSQL)

| Component | Minimum    | Recommended | Notes                                                                         |
| --------- | ---------- | ----------- | ----------------------------------------------------------------------------- |
| CPU       | 4 cores    | 8–16 cores  | Aggregation queries, index maintenance, bulk inserts                          |
| RAM       | 16 GB      | 32–64 GB    | Shared buffers should be ~25% of RAM; working memory for sorts and hash joins |
| Storage   | 200 GB SSD | 500 GB SSD  | See storage breakdown below                                                   |
| IOPS      | 3,000      | 10,000+     | Bulk insert phases are write-heavy                                            |

**Database storage breakdown (per N documents):**

| Table                           | Row Count    | Est. Row Size                                 | Est. Total (at 1M docs) |
| ------------------------------- | ------------ | --------------------------------------------- | ----------------------- |
| Documents (text inline)         | N            | 10–50 KB (with OriginalText + NormalizedText) | 10–50 GB                |
| Documents (text externalized)   | N            | 500 bytes (hashes + metadata only)            | ~500 MB                 |
| MinHashSignatures               | N            | 400 bytes (100 × int32)                       | ~400 MB                 |
| LshBuckets                      | N × 20 bands | 60 bytes                                      | ~1.2 GB                 |
| DocumentGroups                  | ~N/2         | 200 bytes                                     | ~100 MB                 |
| DocumentGroupMemberships        | N            | 80 bytes                                      | ~80 MB                  |
| Indexes (B-tree on hashes, FKs) | —            | —                                             | 30–50% of table size    |
| **Total (text inline)**         |              |                                               | **~20–75 GB**           |
| **Total (text externalized)**   |              |                                               | **~4–8 GB**             |

> Scale linearly once document count is known. Actual text size per document depends on content.

**Cloud managed database equivalents:**

| Provider    | Service                                | Config                      | Approx. Cost/mo           |
| ----------- | -------------------------------------- | --------------------------- | ------------------------- |
| Azure       | Azure Database for PostgreSQL Flexible | 8 vCores, 64 GB RAM, 512 GB | ~$500–$700                |
| AWS         | RDS PostgreSQL                         | db.r6g.2xlarge, 500 GB gp3  | ~$400–$600                |
| Self-hosted | VM + PostgreSQL                        | Same specs as above         | ~$200–$400 (VM cost only) |

### 1.3 File / Blob Storage

| Item                             | Volume               | Notes                               |
| -------------------------------- | -------------------- | ----------------------------------- |
| Phase 1 PDFs                     | 13 TB                | Golden truth / reference collection |
| Phase 2 PDFs                     | 8 TB                 | Comparison collection               |
| Extracted text (if externalized) | Depends on doc count | Plain text, highly compressible     |
| **Total**                        | **~21 TB**           |                                     |

| Provider  | Service              | Cost/mo (per TB)                     | Est. Total/mo |
| --------- | -------------------- | ------------------------------------ | ------------- |
| Azure     | Blob Storage (Hot)   | ~$20                                 | ~$420         |
| Azure     | Blob Storage (Cool)  | ~$10                                 | ~$210         |
| AWS       | S3 Standard          | ~$23                                 | ~$483         |
| AWS       | S3 Infrequent Access | ~$12.50                              | ~$263         |
| Local NAS | —                    | One-time ~$3,000–$6,000 (24+ TB raw) | $0 ongoing    |

### 1.4 Network / Data Transfer

| Item                                  | Volume                      | Est. Cost                           |
| ------------------------------------- | --------------------------- | ----------------------------------- |
| Upload 21 TB to cloud (if applicable) | 21 TB                       | Free (ingress is free on AWS/Azure) |
| Egress for results/reports            | Minimal (<10 GB)            | Negligible                          |
| Cross-AZ traffic (DB <-> compute)     | 20–100 GB during processing | $1–$10                              |
| **Total data transfer**               |                             | **~$1–$10**                         |

### 1.5 Total Infrastructure Cost Summary

**For a single processing run (optimized architecture):**

| Category                            | One-Time | Monthly (while active) | Per-Run                                  |
| ----------------------------------- | -------- | ---------------------- | ---------------------------------------- |
| Compute (cloud VM)                  | —        | —                      | $12–$25 (4–8 hr run)                     |
| PostgreSQL (managed)                | —        | $400–$700              | Prorated                                 |
| File storage (21 TB)                | —        | $210–$483              | —                                        |
| Data transfer                       | —        | —                      | $1–$10                                   |
| **Total for single run**            |          |                        | **$350–$700** (assuming 1 week of infra) |
| **Total if infra stays up 1 month** |          |                        | **$625–$1,200**                          |

**For ongoing/repeated processing:**

| Scenario                                   | Est. Monthly Cost          |
| ------------------------------------------ | -------------------------- |
| Infrastructure always on, periodic re-runs | $625–$1,200/mo             |
| Spin up on demand, tear down after         | $350–$700 per run          |
| Self-hosted hardware (amortized)           | $150–$300/mo + electricity |

---

### 1.6 Recommended AWS Architecture

The team will use AWS for this project. The architecture below is designed for developer convenience, with UI for feedback/status/performance, and clear separation between persistent storage, compute, and the application layer.

#### Infrastructure Layout

| Component                | AWS Service                                                              | Instance/Config                                     | Purpose                                                                                                                    |
| ------------------------ | ------------------------------------------------------------------------ | --------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------- |
| File storage (21 TB)     | **S3**                                                                   | Standard tier                                       | Permanent home for all PDFs and extracted text files. Cheap, durable, unlimited capacity.                                  |
| Blazor app host          | **EC2**                                                                  | t3.medium (~$30/mo)                                 | Hosts the web UI for job submission, status, results browsing. Always on, lightweight.                                     |
| Database                 | **RDS PostgreSQL**                                                       | db.r6g.xlarge (4 vCPU, 32 GB RAM, 500 GB gp3)       | Managed PostgreSQL with automated backups. Includes pgvector extension for RAG if needed.                                  |
| Processing compute       | **AWS Batch**                                                            | c7i.8xlarge (compute-optimized, 32 vCPU, 64 GB RAM) | Text extraction, hashing, MinHash, Jaccard, embedding generation. Spins up on demand, terminates when done — no idle cost. |
| Processing local storage | **NVMe instance store**                                                  | 1–2 TB (attached to Batch workers)                  | Fast scratch disk. Workers pull from S3, process locally, write results to DB, then terminate.                             |
| Embedding model          | **Amazon Bedrock**                                                       | Titan Embeddings v2 or Cohere Embed                 | Pay-per-token, no infrastructure to manage. Used for RAG layer (§2.3.5).                                                   |
| Vector search            | **pgvector on RDS** (start here) or **OpenSearch Serverless** (scale up) | —                                                   | Start with pgvector in the existing Postgres. Migrate to OpenSearch if needed.                                             |
| Container registry       | **ECR**                                                                  | —                                                   | Stores Docker images for the processing workers.                                                                           |

#### Data Flow

```
┌─────────────────────────────────────────────────────────────┐
│                         S3 Bucket                           │
│  /phase1-pdfs/  (13 TB)                                     │
│  /phase2-pdfs/  (8 TB)                                      │
│  /extracted-text/phase1/  (persisted .txt files)            │
│  /extracted-text/phase2/  (persisted .txt files)            │
└──────────┬──────────────────────────────────┬───────────────┘
           │                                  │
           ▼                                  ▼
┌─────────────────────┐          ┌────────────────────────────┐
│   AWS Batch Workers │          │     Blazor App (EC2)       │
│   (c7i.8xlarge)     │          │                            │
│                     │          │  • Job submission          │
│  • Pull PDFs from S3│          │  • Real-time progress      │
│  • Extract text     │          │    (SignalR)               │
│  • Hash / MinHash   │          │  • Results browser         │
│  • Embed (Bedrock)  │          │  • Performance dashboard   │
│  • Write results    │          │                            │
│    to RDS           │          └─────────────┬──────────────┘
│  • Push .txt to S3  │                        │
└──────────┬──────────┘                        │
           │                                   │
           ▼                                   ▼
┌─────────────────────────────────────────────────────────────┐
│                    RDS PostgreSQL                            │
│  • Document metadata, hashes, MinHash signatures            │
│  • LSH buckets                                              │
│  • Document groups + memberships                            │
│  • pgvector embeddings (for RAG layer)                      │
│  • Job status / progress records                            │
└─────────────────────────────────────────────────────────────┘
```

#### UI / Feedback / Status / Performance — Three Layers

1. **Blazor app** (custom business UI — already exists, needs enhancement):
   
   - Job submission dashboard — kick off Phase 1 indexing, Phase 2 comparison
   - Real-time progress via SignalR (Blazor Server already supports this): files processed, current phase, bytes completed, estimated time remaining
   - Results browser — groups found, match confidence, unmatched files
   - This is the primary interface for the team

2. **CloudWatch Dashboards** (infrastructure-level monitoring, included with AWS):
   
   - CPU/memory utilization on Batch workers
   - S3 read/write throughput (are we I/O bottlenecked?)
   - RDS query latency, connections, IOPS
   - Custom metrics pushed from the app (docs/sec, pairs verified/sec, embeddings/sec)
   - This is the "are we healthy and performing well" view

3. **AWS Batch Console** (job-level monitoring, included with AWS):
   
   - Visual job queue status — succeeded, failed, running counts
   - Log streaming from each worker via CloudWatch Logs
   - Automatic retry handling for failed jobs

#### Developer Experience

| Tool                                | Purpose                                                                                                                                       |
| ----------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------- |
| **AWS CDK (C#)**                    | Infrastructure as code in a language the team already knows. Define the entire stack (Batch, RDS, S3, EC2) in C# and deploy with one command. |
| **ECR**                             | Push Docker images for processing workers. Batch pulls from here.                                                                             |
| **CodePipeline + CodeBuild**        | CI/CD from GitHub — auto-build and deploy on push.                                                                                            |
| **Systems Manager Session Manager** | Secure shell access to EC2 instances without managing SSH keys or opening ports.                                                              |
| **VS Code Remote SSH**              | Developers can code directly on the EC2 host if needed.                                                                                       |

#### Estimated Monthly Cost (AWS)

| Service                             | Config                             | Est. Cost/mo                                |
| ----------------------------------- | ---------------------------------- | ------------------------------------------- |
| S3 (21 TB + extracted text)         | Standard                           | ~$500                                       |
| RDS PostgreSQL                      | db.r6g.xlarge, 500 GB gp3          | ~$250                                       |
| EC2 Blazor host                     | t3.medium, always on               | ~$30                                        |
| AWS Batch compute                   | c7i.8xlarge, on-demand as needed   | Per processing hour (~$1.36/hr)             |
| Bedrock embeddings                  | Titan v2, pay per token            | Depends on doc count and hash pre-filtering |
| CloudWatch                          | Dashboards + custom metrics + logs | ~$10–$30                                    |
| ECR + CodePipeline                  | —                                  | ~$5–$10                                     |
| **Total (idle, between runs)**      |                                    | **~$790/mo**                                |
| **Total (during a processing run)** |                                    | **~$800–$850/mo + Bedrock token costs**     |

> Most of the idle cost is S3 storage (~$500) and RDS (~$250). If the database can be stopped between runs, idle cost drops to ~$540/mo (S3 + EC2 only).

---

## 2. Application Architecture Changes Required

### Overview

The current application is designed for interactive use with small-to-medium document sets (hundreds to low thousands). Processing 21 TB of documents requires changes at every layer. The changes below are grouped into three tiers by priority.

---

### Tier 1 — Critical (Application Will Not Function Without These)

#### 2.1.1 Streaming / Batched Document Loading

**Current behavior:**
`GroupingOrchestrator.GroupAllDocumentsAsync()` calls `GetAllAsync()` which loads all documents — including full `OriginalText` and `NormalizedText` fields — into memory via Entity Framework. At any significant document volume this will crash with an `OutOfMemoryException`.

**Required change:**
Replace bulk in-memory loading with phase-appropriate data access:

- **Phase 1 (exact hash grouping):** Execute grouping entirely in SQL:
  
  ```sql
  SELECT "TextHash", array_agg("Id")
  FROM "Documents"
  GROUP BY "TextHash"
  HAVING COUNT(*) > 1
  ```
  
  No need to load document text at all. Only retrieve IDs and hashes.

- **Phase 2 (fuzzy hash grouping):** Same approach — `GROUP BY FuzzyHash` in SQL.

- **Phase 3 (LSH similarity):** Load only document IDs and MinHash signatures (not text). Stream in batches of 10,000–50,000. Only load full text for the specific candidate pairs that need Jaccard verification.

- **Phase 4 (singletons):** A single SQL query identifies ungrouped document IDs:
  
  ```sql
  SELECT "Id" FROM "Documents"
  WHERE "Id" NOT IN (SELECT "DocumentId" FROM "DocumentGroupMemberships")
  ```

**Estimated effort:** Medium. Requires rewriting the data access layer for each phase and adding projection queries (select specific columns, not full entities).

---

#### 2.1.2 Background Job System

**Current behavior:**
The `POST /api/documents/process` endpoint calls `GroupAllDocumentsAsync()` synchronously on the HTTP request thread. The request would time out (default 30–120 seconds) long before processing completes. There is no way to track progress, resume after failure, or cancel a running job.

**Required change:**
Add a background processing system with job tracking:

- **Option A — ASP.NET Hosted Service + Channel:**
  Simplest. The API endpoint enqueues a job to a `Channel<T>`, a `BackgroundService` picks it up. State stored in the `ProcessingJobs` table (which already exists in the schema). Good for single-server deployment.

- **Option B — Hangfire:**
  More robust. Provides job persistence, retry, dashboard, and scheduled jobs out of the box. Uses PostgreSQL for storage (already available). Better for production.

- **Option C — MassTransit + RabbitMQ:**
  Most scalable. Supports distributed workers across multiple machines. Required only if processing needs to fan out across multiple servers.

Regardless of choice, the API changes to:

```
POST /api/documents/process  → returns { "jobId": "abc-123" }
GET  /api/jobs/{id}/status   → returns { "phase": 3, "progress": 45.2, "bytesProcessed": "2.1 TB" }
DELETE /api/jobs/{id}        → cancels a running job
```

Each pipeline phase should report progress (data processed, pairs verified, groups created) to the job record so the UI can display a progress indicator.

**Estimated effort:** Medium. The `ProcessingJobs` entity already exists. The main work is wiring up the background service and adding progress reporting to each phase.

---

#### 2.1.3 Batch Database Writes

**Current behavior:**
`SaveChangesAsync()` is called after every individual group is created. Each group also inserts its membership records one at a time. At scale, this means potentially millions of individual `SaveChangesAsync()` calls.

At 1ms per round-trip, even hundreds of thousands of calls translates to **minutes or hours of pure database I/O** — and that's optimistic. Real-world latency with connection overhead pushes this higher.

**Required change:**
Batch group and membership inserts:

- Accumulate groups in a list (1,000–5,000 per batch)

- Call `AddRange()` + single `SaveChangesAsync()` per batch

- For maximum throughput, use `EFCore.BulkExtensions` or raw `COPY` commands:
  
  ```csharp
  await context.BulkInsertAsync(groupBatch);
  await context.BulkInsertAsync(membershipBatch);
  ```

- Disable EF Core change tracking during bulk operations (`context.ChangeTracker.AutoDetectChangesEnabled = false`)

**Expected improvement:** Reduces millions of database calls to hundreds of batch calls. Database I/O drops from hours to seconds.

**Estimated effort:** Low–Medium. Straightforward batching logic plus adding a bulk insert library.

---

### Tier 2 — Required for Reasonable Processing Time

#### 2.2.1 Persist MinHash Signatures and LSH Buckets

**Current behavior:**
MinHash signatures (100 integer values per document) are computed in memory during Phase 3 and discarded when processing completes. The database schema already has `MinHashSignatures` and `LshBuckets` tables, but they are never written to. Every processing run — or even adding a single new document — recomputes all signatures from scratch.

**Required change:**

- After computing a document's MinHash signature, persist it to the `MinHashSignatures` table
- After computing LSH bucket assignments, persist them to the `LshBuckets` table
- On subsequent runs, load existing signatures from the database instead of recomputing
- Only compute signatures for new/modified documents
- When adding new documents, query existing LSH buckets for candidates instead of recomputing everything

**Expected improvement:** Eliminates redundant computation on re-runs. First run: same cost. Subsequent runs with new documents: only process the new additions. Reduces incremental processing from hours to minutes.

**Estimated effort:** Low–Medium. Tables already exist; need to add write logic after computation and read logic before Phase 3.

---

#### 2.2.2 Incremental Processing

**Current behavior:**
`GroupAllDocumentsAsync()` begins by calling `DeleteAllAsync()` — deleting every existing group and membership — then reprocesses all documents from scratch. There is no concept of "process only new documents."

**Required change:**

- Add a `ProcessedAt` timestamp (or version number) to the `Document` entity
- Track which documents have been grouped and which are new
- New processing runs should only:
  1. Compute hashes/signatures for unprocessed documents
  2. Check new documents against existing hash indexes and LSH buckets
  3. Merge new documents into existing groups or create new groups
- Full reprocessing should only happen when rules or similarity thresholds change
- Add an API parameter: `POST /api/documents/process?mode=incremental` vs `mode=full`

**Expected improvement:** After initial load, adding batches of new documents becomes a fast operation (minutes instead of hours). Critical for operational workflows where documents arrive continuously.

**Estimated effort:** Medium. Requires changes to the orchestrator, repository queries, and API contracts.

---

#### 2.2.3 Efficient Union-Find for Group Merging

**Current behavior:**
After Phase 3 verifies candidate pairs, the merge logic uses a naive approach: for each verified pair, it checks both documents' current group assignments, then copies one group's member list into the other using `List.AddRange()` and re-maps every member. This is O(n) per merge, and merging two large groups (e.g., 1,000 members each) copies 1,000 elements.

With millions of verified pairs, the single-threaded merge loop becomes a bottleneck measured in hours.

**Required change:**
Replace with weighted union-find (disjoint set) with path compression:

```csharp
// Standard union-find with path compression and union by rank
private int[] parent;
private int[] rank;

int Find(int x) {
    if (parent[x] != x)
        parent[x] = Find(parent[x]);  // Path compression
    return parent[x];
}

void Union(int a, int b) {
    int ra = Find(a), rb = Find(b);
    if (ra == rb) return;
    if (rank[ra] < rank[rb]) (ra, rb) = (rb, ra);
    parent[rb] = ra;
    if (rank[ra] == rank[rb]) rank[ra]++;
}
```

This brings amortized merge cost to nearly O(1) per pair (inverse Ackermann function). After all pairs are merged, a single pass over the parent array extracts final groups.

**Expected improvement:** Merge phase drops from hours to seconds for millions of pairs.

**Estimated effort:** Low. Well-known algorithm, straightforward replacement.

---

#### 2.2.4 Compiled Regex Cache

**Current behavior:**
`TextNormalizer.Normalize()` calls `Regex.Replace()` approximately 15 times per document using inline pattern strings. Each call compiles the regex pattern from scratch. At scale, this results in tens of millions of redundant regex compilations.

**Required change:**
Use .NET source-generated regex (available since .NET 7):

```csharp
[GeneratedRegex(@"\b0([a-z])", RegexOptions.Compiled)]
private static partial Regex OcrZeroToO();

[GeneratedRegex(@"\bpage\s+\d+(\s+of\s+\d+)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
private static partial Regex PageNumberPattern();
```

Or at minimum, declare patterns as `static readonly Regex` fields with `RegexOptions.Compiled`.

**Expected improvement:** ~5–10% reduction in total CPU time. Simple change with no behavioral impact.

**Estimated effort:** Low. Mechanical refactor — change each inline `Regex.Replace()` to use a cached pattern.

---

### Tier 3 — Production Readiness and Operational Efficiency

#### 2.3.1 Staged Pipeline Architecture

**Current behavior:**
The entire processing pipeline runs as a single method call (`GroupAllDocumentsAsync`). If it fails at Phase 3 after completing Phases 1 and 2, all progress is lost and processing must restart from the beginning.

**Required change:**
Decompose into independently restartable stages, each reading from and writing to the database:

```
Stage 1: Ingest & Extract Text
  Input:  Raw PDF files
  Output: Document records with OriginalText, FileHash
  Parallelism: High (one worker per file, multiple workers)

Stage 2: Normalize & Hash
  Input:  Documents with OriginalText, no NormalizedText
  Output: NormalizedText, TextHash, FuzzyHash
  Parallelism: High (independent per document)

Stage 3: Compute MinHash Signatures
  Input:  Documents with NormalizedText, no MinHashSignature
  Output: MinHashSignature records (100 values per doc)
  Parallelism: High (independent per document)

Stage 4: Build LSH Buckets
  Input:  MinHashSignatures
  Output: LshBucket records (20 bands per doc)
  Parallelism: Medium (band computation is independent, bucket writes need coordination)

Stage 5: Exact & Fuzzy Hash Grouping (Phases 1–2)
  Input:  TextHash, FuzzyHash columns
  Output: DocumentGroup + Membership records
  Parallelism: Low (SQL aggregation, fast)

Stage 6: Generate Candidate Pairs from LSH
  Input:  LshBucket records
  Output: Candidate pair table (doc_id_1, doc_id_2)
  Parallelism: Medium (per-band query parallelism)

Stage 7: Verify Candidate Pairs (Jaccard)
  Input:  Candidate pairs + NormalizedText for those specific documents
  Output: Verified pairs with similarity scores
  Parallelism: High (independent per pair, biggest CPU consumer)

Stage 8: Merge Groups (Union-Find)
  Input:  Verified pairs + existing groups from Stage 5
  Output: Final DocumentGroup + Membership records
  Parallelism: Low (single-threaded union-find, but fast with proper algorithm)
```

Each stage:

- Tracks its own progress in the `ProcessingJobs` table
- Can be retried independently without rerunning prior stages
- Reads its input from the database (checkpointed state)
- Writes its output to the database before the next stage begins

**Expected improvement:** Fault tolerance — a crash at Stage 7 doesn't lose Stages 1–6. Also enables running Stages 1–3 on multiple worker machines in parallel.

**Estimated effort:** High. Requires decomposing the monolithic orchestrator into separate stage handlers with a stage coordinator.

---

#### 2.3.2 Database Query Optimization

**Current behavior:**
Several queries are significantly inefficient at scale:

- `GetStatisticsAsync()` loads all group entities (with navigation properties) into memory just to count them and compute aggregates.
- `GetAllAsync()` on groups eagerly includes all `Memberships` and their `Document` navigation properties — loading the entire database into memory.
- No indexes exist on `TextHash`, `FuzzyHash`, or LSH bucket columns.

**Required changes:**

1. **Statistics via SQL aggregation:**
   
   ```sql
   SELECT
     COUNT(*) as total_groups,
     SUM("DocumentCount") as total_docs,
     COUNT(*) FILTER (WHERE "Confidence" = 'VeryHigh') as very_high_count,
     COUNT(*) FILTER (WHERE "Confidence" = 'High') as high_count,
     COUNT(*) FILTER (WHERE "Confidence" = 'Medium') as medium_count,
     COUNT(*) FILTER (WHERE "DocumentCount" = 1) as singleton_count
   FROM "DocumentGroups"
   ```

2. **Paginated group retrieval** with server-side filtering:
   
   ```
   GET /api/groups?page=1&pageSize=50&confidence=High
   ```

3. **Add database indexes:**
   
   ```sql
   CREATE INDEX ix_documents_texthash ON "Documents" ("TextHash");
   CREATE INDEX ix_documents_fuzzyhash ON "Documents" ("FuzzyHash");
   CREATE INDEX ix_lshbuckets_bandhash ON "LshBuckets" ("BandIndex", "BucketHash");
   CREATE INDEX ix_memberships_docid ON "DocumentGroupMemberships" ("DocumentId");
   ```

**Expected improvement:** Statistics queries drop from seconds (loading all entities) to milliseconds. Group browsing becomes usable. LSH candidate generation becomes index-driven.

**Estimated effort:** Low–Medium. Mostly adding raw SQL queries or EF projections plus a migration for indexes.

---

#### 2.3.3 Externalize Document Text Storage

**Current behavior:**
Both `OriginalText` and `NormalizedText` are stored as columns in the `Documents` table. At scale this adds tens of gigabytes or more to the database. Every query that touches the `Documents` table risks pulling this data into memory, and PostgreSQL TOAST compression has limits.

**Required change (choose one):**

- **Option A — Blob storage:** Store text in Azure Blob Storage or S3. Document record holds only a path/URI. Text is fetched on-demand during Jaccard verification.
  
  - Pro: Database stays small. Cheap storage.
  - Con: Network latency for text retrieval during pair verification.

- **Option B — Compressed database column:** GZip-compress text before storing. Decompress on read.
  
  - Pro: No external dependency. ~70–80% size reduction.
  - Con: CPU overhead for compression/decompression. Database still larger than Option A.

- **Option C — Separate text table:** Move text columns to a `DocumentTexts` table with a 1:1 FK. Main `Documents` table stays lightweight for hash/metadata queries.
  
  - Pro: Simple. Queries that don't need text are fast. No external dependencies.
  - Con: Database still holds all text (just in a separate table).

**Recommended:** Option C as a quick win, potentially combined with Option A for very large deployments.

**Estimated effort:** Low (Option C) to Medium (Option A).

---

#### 2.3.4 Cross-Collection Comparison Mode

**Current behavior:**
The application treats all documents as a single pool and groups them by similarity. There is a "classify against canonicals" feature, but it loads all documents and all canonicals into memory and compares every document against every canonical sequentially.

**Required change for the Phase 1-vs-Phase 2 scenario:**

Phase 1 (13 TB) is the **golden truth / reference collection**. Phase 2 (8 TB) must be compared against it. The comparison is **one-directional and file-by-file**: each individual Phase 2 document is independently compared against the Phase 1 index. Phase 1 documents are **never compared against each other** — they are the established truth and only serve as the reference that Phase 2 is matched against.

1. **Bucketize all files by metadata (document type, etc.) — this is step one:**
   
   - Before any text extraction or comparison, classify every file in both Phase 1 and Phase 2 into metadata buckets (e.g., by document type or other available metadata)
   - This is the primary partitioning step — all subsequent processing happens within buckets, not across the full collection
   - Bucketing can reduce the effective comparison scope by 10–100x before any text work begins

2. **Fully ingest and index Phase 1 (golden truth) within each bucket — prerequisite before any Phase 2 processing:**
   
   - Within each metadata bucket, ingest all Phase 1 documents: extract text, normalize, compute hashes, MinHash signatures, and LSH buckets
   - This is a one-time operation; all results are persisted to the database
   - The entire Phase 1 index must be built and available before Phase 2 processing begins

3. **Process each Phase 2 file individually against the Phase 1 index in its bucket:**
   
   - For **each individual Phase 2 file**:
     - Determine its metadata bucket (document type, etc.)
     - Extract text, normalize, compute hashes and MinHash signature
     - Within that bucket, query the Phase 1 LSH index for candidate matches
     - Verify top candidates with Jaccard similarity
     - Assign to an existing Phase 1 group or flag as unmatched
   - This is a **file-by-file** operation, not a bulk batch — each Phase 2 document is processed and resolved independently
   - **Phase 2 documents are NEVER compared against each other** — only against Phase 1 golden truth

> **Important — golden truth only comparison:**
> Within a bucket, every Phase 2 document is compared exclusively against Phase 1 (golden truth) documents. Phase 2 documents are never compared to other Phase 2 documents, even if they might be similar to each other.
> 
> **Example:** In a bucket, Phase 1 doc A is the golden truth reference.
> 
> - Phase 2 doc B arrives → compared to A → similar → matched to A's group
> - Phase 2 doc C arrives → compared to A → not similar → flagged as **unmatched**
> - Doc C is **not** compared to doc B, even though B and C might be similar to each other
> 
> This is by design. The question being answered is: *"Does this Phase 2 file have a match in the golden truth?"* — not *"Which Phase 2 files are similar to each other?"*

4. **Two layers of narrowing eliminate the combinatorial explosion:**
   
   - **Metadata bucketing** (step 1 filter): Only compare a Phase 2 document against Phase 1 documents in the same bucket. Reduces the candidate pool by 10–100x before any text comparison.
   - **LSH bucketing** (step 3 filter): Within the metadata bucket, LSH narrows to ~10–50 candidates per document.
   - Net result: each Phase 2 file compares against a small handful of Phase 1 candidates, not the entire collection.
   - **Reduction factor: orders of magnitude (easily 10,000x–100,000x+) vs. all-pairs**

**Expected improvement:** Processing time drops from days to hours. Memory requirements are modest since only one Phase 2 document is in memory at a time alongside the pre-built Phase 1 index. Phase 2 is the smaller collection (8 TB vs 13 TB), so streaming it file-by-file against the index is the optimal direction.

**Estimated effort:** Medium–High. Requires a new orchestration mode (Phase 1 full-index then Phase 2 file-by-file), metadata bucket partitioning, changes to the API (specify Phase 1 vs Phase 2 collection), and modifications to the LSH query logic.

---

#### 2.3.4.1 Design Consideration: Future Phase 2-to-Phase 2 Comparison

The current design compares Phase 2 documents only against Phase 1 golden truth. But if the client later asks *"which Phase 2 documents are similar to each other?"* (e.g., comparing doc C to doc B), the architecture needs a Phase 2 index — which doesn't exist in the golden-truth-only design.

**Options if this requirement emerges:**

| Option                               | Approach                                                                                                                                         | Effort to Add   | Trade-off                                                                                                                                    |
| ------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------ | --------------- | -------------------------------------------------------------------------------------------------------------------------------------------- |
| **A. Second pass (recommended)**     | After all Phase 2 files are compared against Phase 1, take the **unmatched** Phase 2 docs and run a separate grouping pass among themselves      | Low             | Clean separation. Only adds time for unmatched docs. Easy to bolt on without touching the existing pipeline.                                 |
| **B. Build Phase 2 index as you go** | As each Phase 2 doc is processed, also add it to a growing Phase 2 index. Each subsequent Phase 2 doc checks both Phase 1 AND the Phase 2 index. | Medium          | Breaks file-by-file independence — processing order now matters, and the index grows during the run. More complex, but only one pass needed. |
| **C. Full combined grouping**        | Treat both phases as one collection and do all-pairs grouping.                                                                                   | High (redesign) | Throws out the golden-truth-only constraint entirely. Major rework of the current architecture.                                              |

**Decision: persist Phase 2 index data now (future-proofing confirmed):**

During Phase 2 processing, the system already computes hashes, MinHash signatures, and embeddings for each file. Rather than discarding this data after comparison, **all Phase 2 index data will be written to the database alongside the comparison results**. The golden-truth comparison logic remains unchanged — Phase 2 index data is not queried during the current run.

This costs almost nothing extra (the data is computed anyway) and means a complete Phase 2 index is already built and ready if Phase 2-to-Phase 2 comparison (Option A — second pass) is ever requested. No rework or reprocessing required — just enable the second pass.

---

#### 2.3.5 RAG-Based Comparison Using Embeddings

The MinHash/LSH/Jaccard approach (§2.3.4) catches textually similar documents — same words in roughly the same order. But it will miss documents that express the **same content in different wording**. A RAG (Retrieval-Augmented Generation) approach using vector embeddings adds semantic similarity, catching matches that text-based methods cannot.

**Recommended hybrid approach — use both layers:**

| Layer                | Method                      | What it catches                                      | Cost                         |
| -------------------- | --------------------------- | ---------------------------------------------------- | ---------------------------- |
| 1. Exact hash        | SHA-256 of normalized text  | Identical documents                                  | Free (CPU only)              |
| 2. Fuzzy hash        | SimHash / similar           | Near-identical with minor differences                | Free (CPU only)              |
| 3. RAG vector search | Embedding cosine similarity | Semantically similar (same meaning, different words) | Embedding API cost per chunk |

Cheap hashing handles the easy cases first. Only documents that don't match via hashing proceed to the embedding layer, minimizing API costs.

**RAG pipeline:**

1. **Extract text** → persist `.txt` files to S3 (see §0)
2. **Chunk** each document into segments (paragraphs, pages, or fixed-size windows)
3. **Generate embeddings** for each chunk using an embedding model
4. **Store embeddings** in a vector database
5. **For each Phase 2 file** (that wasn't already matched by hashing): chunk it, embed it, query the vector store for the most similar Phase 1 chunks/documents
6. **Score and group** based on similarity thresholds

**AWS services for the RAG layer:**

| Layer                     | Service                                               | Notes                                                                                     |
| ------------------------- | ----------------------------------------------------- | ----------------------------------------------------------------------------------------- |
| Text storage              | S3                                                    | Persisted extracted text from §0                                                          |
| Embedding model           | Amazon Bedrock (Titan Embeddings v2, or Cohere Embed) | Pay per token, no infrastructure to manage                                                |
| Vector store — simple     | **pgvector extension on RDS PostgreSQL**              | Keeps everything in one database; good for moderate scale                                 |
| Vector store — high scale | **Amazon OpenSearch Serverless**                      | Purpose-built for vector search; faster ANN queries at large scale                        |
| Fully managed option      | **Amazon Bedrock Knowledge Bases**                    | Point at an S3 bucket — handles chunking, embedding, indexing, and querying automatically |
| Orchestration             | AWS Batch or Step Functions                           | Manage the extract → chunk → embed → query pipeline                                       |

> **pgvector vs OpenSearch:** Start with pgvector since the app already uses PostgreSQL. If vector query performance becomes a bottleneck, migrate to OpenSearch Serverless. Bedrock Knowledge Bases is the fastest path to a working prototype — it handles the entire pipeline from S3 to queryable index.

**RAG vs MinHash/LSH/Jaccard comparison:**

| Aspect                          | MinHash/LSH/Jaccard         | RAG (Embeddings + Vector Search)      |
| ------------------------------- | --------------------------- | ------------------------------------- |
| Textually similar (same words)  | Excellent                   | Good                                  |
| Exact duplicates                | Excellent (hash match)      | Overkill — use hashing                |
| Near-duplicates                 | Good (fuzzy hash + Jaccard) | Good (high cosine similarity)         |
| Different wording, same content | **Misses these**            | **Catches these**                     |
| Per-document cost               | Free (CPU only)             | Embedding API cost per chunk          |
| Index query speed               | Fast (LSH bucket lookup)    | Fast (ANN vector search)              |
| Infrastructure complexity       | Low (CPU + PostgreSQL)      | Medium (embedding API + vector store) |

**Embedding cost estimate:**

| Item                        | Estimate                                    | Notes                                         |
| --------------------------- | ------------------------------------------- | --------------------------------------------- |
| Titan Embeddings v2 pricing | ~$0.00002 per 1K tokens                     |                                               |
| Avg text per document       | ~2K–10K tokens (rough, depends on doc size) |                                               |
| Cost to embed 1 document    | ~$0.00004–$0.0002                           |                                               |
| Phase 1 indexing (one-time) | Depends on document count                   | Every document that wasn't matched by hashing |
| Phase 2 querying            | Depends on document count                   | Only unmatched documents from hash layers     |

> Exact cost depends on document count (not yet known) and how many documents get resolved by the cheaper hash layers first. The hybrid approach minimizes embedding spend by only sending the hard cases to RAG.

**Estimated effort:** Medium. Bedrock Knowledge Bases can provide a working prototype quickly. A custom pipeline (Bedrock embeddings + pgvector) gives more control but requires more development. The existing Blazor app needs a new comparison mode and result display for RAG-based matches.

---

## 3. Client Deliverable: Group ID Tagging

### 3.1 Approach

Rather than providing a reconciliation UI for the client, the deliverable is a **group ID tag** assigned to each processed document. Documents that match a Phase 1 golden truth document share the same group ID, giving the client's downstream systems a way to link related files together.

Each group ID is **anchored to a Phase 1 golden truth document**. Since Phase 1 is stable and doesn't change, group IDs remain consistent across re-runs. New Phase 2 documents added in future runs get assigned to existing groups without changing prior IDs.

### 3.2 Output Schema Per Document

Every processed document receives a full result record — never a bare group ID alone.

| Field                  | Example                                                      | Purpose                                                             |
| ---------------------- | ------------------------------------------------------------ | ------------------------------------------------------------------- |
| Document ID / filename | `phase2/doc-12345.pdf`                                       | Which file                                                          |
| Group ID               | `grp-a1b2c3d4`                                               | Links similar files together. Anchored to Phase 1 golden truth doc. |
| Status                 | `matched` / `unmatched`                                      | Explicit outcome — never blank                                      |
| Confidence tier        | `high` / `medium` / `low`                                    | How trustworthy the match is (see thresholds below)                 |
| Match score            | `0.87`                                                       | Raw similarity score (Jaccard or cosine)                            |
| Match method           | `exact_hash` / `fuzzy_hash` / `lsh_jaccard` / `rag_semantic` | Which comparison layer found the match                              |
| Matched Phase 1 doc    | `phase1/golden-789.pdf`                                      | The specific golden truth document it matched against               |
| Bucket                 | `birth_certificates`                                         | Metadata category                                                   |

### 3.3 Confidence Tiers

Rather than a single pass/fail threshold, results are delivered in tiers so the client can decide how to handle edge cases in their own systems.

| Tier          | Criteria                                       | Client guidance                                       |
| ------------- | ---------------------------------------------- | ----------------------------------------------------- |
| **High**      | Exact hash, fuzzy hash, or >90% Jaccard/cosine | Treat as confirmed match — safe to auto-link          |
| **Medium**    | 70–90% similarity                              | Likely a match — link with review flag if desired     |
| **Low**       | 50–70% similarity                              | Possible match — client should verify before linking  |
| **Unmatched** | Below threshold or no candidates found         | No match in Phase 1 golden truth — flagged explicitly |

> The tier thresholds are configurable and should be agreed upon with the client before the production run. These defaults are starting points.

### 3.4 Design Decisions and Rationale

**Why group IDs, not a reconciliation UI:**

- System-agnostic — works with any downstream system (database, document management, ERP, etc.)
- No dependency on our application after delivery
- Simpler to maintain and re-run

**Why rich tags, not bare IDs:**

- A bare group ID doesn't tell the client whether it's a 99% match or a 71% match
- Including confidence tier, score, and method lets the client make their own decisions about edge cases
- The `unmatched` status is explicit — the client can distinguish "no match found" from "not yet processed"

**Why anchor to Phase 1 golden truth:**

- Group IDs remain stable across re-runs
- If new Phase 2 documents are added later, they join existing groups without changing prior IDs
- If thresholds are adjusted and the process re-runs, the group IDs themselves don't change — only which documents are assigned to them

**Multiple potential matches:**

- The current cascade design assigns each Phase 2 document to its **best match only** (first layer that finds a match, highest score within that layer)
- If the client wants to know about secondary matches (e.g., "85% similar to doc A, 78% similar to doc B"), the schema can be extended to return top-N matches per document
- Default deliverable: best match only, with score and method

### 3.5 Deliverable Format Options

| Format                             | Pros                                                  | Cons                                      | Best for                                            |
| ---------------------------------- | ----------------------------------------------------- | ----------------------------------------- | --------------------------------------------------- |
| **CSV / Parquet export**           | Simplest, universal, works with any tool              | Static snapshot, no live queries          | One-time handoff, data warehouse import             |
| **Database table (direct access)** | Client can query, filter, join with their data        | Requires DB access, ongoing hosting       | Clients with technical teams                        |
| **API endpoint**                   | Pull on demand, always current                        | Requires hosting the app                  | Integration with client's live systems              |
| **Tagged PDF metadata**            | Self-contained — group ID embedded in the file itself | Modifies original files, harder to update | Clients who work with files directly, not databases |

> **Recommendation:** Deliver as **CSV/Parquet** for the initial handoff (simple, no dependencies). If the client needs ongoing integration, add an **API endpoint** to the Blazor app.

---

## 4. Summary

### 4.1 Infrastructure Cost Summary

| Deployment                         | First Run Cost                                 | Ongoing Monthly              |
| ---------------------------------- | ---------------------------------------------- | ---------------------------- |
| Cloud (on-demand, tear down after) | $350–$700                                      | $0 when idle                 |
| Cloud (always on)                  | —                                              | $625–$1,200                  |
| Self-hosted server                 | $5,000–$12,000 (hardware incl. 24+ TB storage) | $75–$150 (power/maintenance) |

### 4.2 Architecture Change Summary

| Change                        | Priority             | Effort      | Impact                                                             |
| ----------------------------- | -------------------- | ----------- | ------------------------------------------------------------------ |
| Streaming document loading    | Tier 1 — Critical    | Medium      | Prevents OOM crash                                                 |
| Background job system         | Tier 1 — Critical    | Medium      | Prevents HTTP timeout; enables progress tracking                   |
| Batch database writes         | Tier 1 — Critical    | Low–Medium  | Reduces DB I/O from hours to seconds                               |
| Persist MinHash/LSH data      | Tier 2 — Performance | Low–Medium  | Eliminates redundant computation on re-runs                        |
| Incremental processing        | Tier 2 — Performance | Medium      | Minutes instead of hours for new document batches                  |
| Union-find algorithm          | Tier 2 — Performance | Low         | Merge phase: hours to seconds                                      |
| Compiled regex                | Tier 2 — Performance | Low         | 5–10% CPU reduction                                                |
| Staged pipeline               | Tier 3 — Production  | High        | Fault tolerance, restartability, multi-machine scaling             |
| Database query optimization   | Tier 3 — Production  | Low–Medium  | UI responsiveness, index-driven LSH queries                        |
| Externalize text storage      | Tier 3 — Production  | Low–Medium  | Smaller database, faster non-text queries                          |
| Cross-collection comparison   | Tier 3 — Production  | Medium–High | Orders-of-magnitude fewer comparisons for Phase 2-vs-Phase 1       |
| RAG-based semantic comparison | Tier 3 — Production  | Medium      | Catches same-meaning/different-wording matches that hashing misses |

### 4.3 Processing Time Estimates

> These estimates assume a large document count (hundreds of thousands to millions). Actual times will depend on final document count, average document size, and text density.

| Architecture State                                                  | Est. Processing Time          |
| ------------------------------------------------------------------- | ----------------------------- |
| Current (no changes)                                                | Will not complete (OOM crash) |
| Tier 1 changes only                                                 | 40–100+ hours                 |
| Tier 1 + Tier 2 changes                                             | 4–8 hours                     |
| All tiers implemented                                               | 1–3 hours                     |
| All tiers + cross-collection mode (Phase 2 vs Phase 1 golden truth) | 30–90 minutes                 |

---

## 5. Future Design: Full Cross-Phase Comparison Mode

### 5.1 Scenario

The current client requires one-directional comparison only: Phase 2 against Phase 1 golden truth. But a future client may want **all documents compared against all other documents** — Phase 1 to Phase 1, Phase 2 to Phase 2, and Phase 1 to Phase 2 — grouping everything that's similar regardless of which phase it came from.

In other words: doc A, doc B, and doc C should all be linked if any of them are similar to each other, even if A is from Phase 1 and B and C are from Phase 2.

### 5.2 What Changes

| Aspect                      | Current Design (golden-truth only)               | Future Design (full cross-phase)               |
| --------------------------- | ------------------------------------------------ | ---------------------------------------------- |
| Phase 1 compared to itself  | No — Phase 1 is static reference                 | **Yes** — Phase 1 docs grouped with each other |
| Phase 2 compared to itself  | No — each file only checks Phase 1               | **Yes** — Phase 2 docs grouped with each other |
| Phase 2 compared to Phase 1 | Yes — one-directional                            | Yes — bidirectional                            |
| Index built for             | Phase 1 only (Phase 2 persisted but not queried) | **Both phases — single unified index**         |
| Group IDs anchored to       | Phase 1 golden truth doc                         | **Any document can be the group anchor**       |
| Comparison direction        | One-directional, file-by-file                    | **All-pairs within buckets**                   |

### 5.3 Impact on Architecture

#### What carries over unchanged

The following investments from the current design apply directly to the full comparison mode with no rework:

- **Text extraction to files (§0)** — same step, same output
- **Metadata bucketing (§2.3.4 step 1)** — still the first filter, still reduces scope
- **Hash layers (exact + fuzzy)** — still the cheapest first pass
- **LSH + MinHash infrastructure** — same algorithm, just applied to more documents
- **RAG / embedding pipeline (§2.3.5)** — same Bedrock + pgvector stack
- **AWS architecture (§1.6)** — same services, possibly larger instances
- **Persisted Phase 2 index data (§2.3.4.1)** — this is already being built, so the Phase 2 index is ready to query immediately

#### What needs to change

| Component                              | Change Required                                                                                                                                                                                                                                         | Effort                                                                                             |
| -------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------- |
| **Phase 1 self-comparison**            | Run the grouping pipeline within Phase 1 before Phase 2 processing. Currently skipped since Phase 1 is treated as static truth.                                                                                                                         | Low — the pipeline already exists, just enable it for Phase 1                                      |
| **Phase 2 self-comparison**            | After comparing Phase 2 against Phase 1, run a second pass comparing unmatched Phase 2 docs against the Phase 2 index. The index is already persisted (§2.3.4.1 future-proofing).                                                                       | Low — enable the second pass query against the persisted Phase 2 index                             |
| **Unified index mode**                 | Instead of separate Phase 1 and Phase 2 indexes, build a single combined index. Each new document (regardless of phase) is compared against the full index, then added to it.                                                                           | Medium — requires a new orchestration mode where every document is both a query and an index entry |
| **Group merging (transitive closure)** | With golden-truth only, groups are simple: each maps to one Phase 1 doc. With full comparison, groups become transitive: if A matches B and B matches C, all three must share a group even if A doesn't directly match C. Requires union-find (§2.2.3). | Low — union-find algorithm is already recommended and handles this natively                        |
| **Group ID assignment**                | Can no longer anchor to "the Phase 1 doc" since a group might contain only Phase 2 docs. Need a strategy: first document in the group, highest-confidence document, or arbitrary stable ID.                                                             | Low — design decision, minimal code                                                                |
| **Deliverable schema (§3.2)**          | The `Matched Phase 1 doc` field no longer applies when two Phase 2 docs match each other. Schema needs a more general `Group members` list.                                                                                                             | Low — extend the output format                                                                     |
| **Processing time**                    | More comparisons. Within each bucket, all-pairs instead of one-directional. LSH keeps this manageable, but expect longer runs.                                                                                                                          | N/A — scaling impact, not code effort                                                              |

### 5.4 Processing Model Comparison

```
CURRENT: Golden-Truth Only              FUTURE: Full Cross-Phase

Phase 1:  Index only (no self-compare)  Phase 1:  Index + group among selves
                │                                       │
                ▼                                       ▼
Phase 2:  Compare each file ──────►     Phase 2:  Compare each file ──────►
          against Phase 1 only                    against FULL index
          (file-by-file)                          (Phase 1 + growing Phase 2)
                │                                       │
                ▼                                       ▼
Result:   Each Phase 2 doc matched      Result:   All docs in all phases
          to one Phase 1 doc (or not)             grouped by similarity
                                                  (transitive closure)
```

### 5.5 Estimated Additional Effort

| Item                             | Effort         | Notes                                           |
| -------------------------------- | -------------- | ----------------------------------------------- |
| Enable Phase 1 self-grouping     | Low            | Turn on existing pipeline for Phase 1           |
| Enable Phase 2 second-pass       | Low            | Query the already-persisted Phase 2 index       |
| Unified index orchestration mode | Medium         | New mode: every doc is query + index entry      |
| Union-find transitive merging    | Low            | Already planned (§2.2.3)                        |
| Updated deliverable schema       | Low            | Generalize group output format                  |
| **Total incremental effort**     | **Low–Medium** | Most infrastructure is already built or planned |

> **Key takeaway:** The current golden-truth-only design was built with this future in mind. The decision to persist Phase 2 index data (§2.3.4.1) means the most expensive part — building the Phase 2 index — is already done. Enabling full cross-phase comparison is primarily an orchestration change, not a rebuild.

### 5.6 Open Decision: One Group Per File vs. Many Groups Per File

> **Status: TO BE DECIDED** — Parking this for future discussion. Does not affect the current golden-truth design (which is naturally one-group-per-file), but must be resolved before implementing full cross-phase grouping.

In full grouping mode, there are two possible models:

| Model | How it works | Pro | Con |
| ----- | ------------ | --- | --- |
| **One group per file (partitioning)** | Each doc belongs to exactly one group. Union-find merges transitively. | Simple deliverable — one group ID per file. Easy for downstream systems. | Transitive chaining risk: A↔B at 85%, B↔C at 75%, C↔D at 71% can create "mega-groups" where A and D end up grouped despite being only ~40% similar to each other. |
| **Many groups per file (overlapping)** | A doc can belong to multiple groups independently. No transitive merging. | No daisy-chaining. Each grouping relationship stands on its own. | More complex deliverable — each file can have multiple group IDs. Client's downstream system must handle one-to-many. |

**Questions to resolve:**
- Does the client's downstream system expect one group ID per file, or can it handle multiple?
- Is transitive chaining (A→B→C→D all in one group) a feature or a bug for the use case?
- Should there be a "chain length" or "minimum pairwise similarity" threshold to prevent mega-groups?
- Would a hybrid work — one primary group, with secondary group memberships listed separately?
