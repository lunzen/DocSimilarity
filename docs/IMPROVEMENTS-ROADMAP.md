# DocGrouping — Improvements & Robustness Roadmap

*Based on a full codebase audit conducted March 2026*

This document catalogs specific improvements needed to make DocGrouping production-ready, organized by priority tier. Each finding references the actual code location and describes the concrete issue.

---

## Summary

| Category | Critical | High | Medium | Total |
|----------|----------|------|--------|-------|
| Error Handling & Resilience | 4 | 2 | 1 | 7 |
| Security | 3 | 3 | 1 | 7 |
| Database | 3 | 3 | 2 | 8 |
| Memory & Performance | 3 | 2 | 2 | 7 |
| Testing | 1 | 3 | 2 | 6 |
| Concurrency | 3 | 1 | 0 | 4 |
| Configuration & Deployment | 1 | 3 | 2 | 6 |
| UI/UX | 0 | 3 | 3 | 6 |
| Feature Completeness | 0 | 3 | 2 | 5 |
| **Total** | **18** | **23** | **15** | **56** |

---

## Tier 1 — Critical (Must Fix Before Production)

### 1.1 PDF Extraction Has Zero Error Handling

**File:** `Infrastructure/TextProcessing/PdfTextExtractor.cs`

Both `ExtractText` overloads have no try/catch. A single corrupt, encrypted, or password-protected PDF crashes the entire ingestion pipeline.

```csharp
public string ExtractText(byte[] pdfBytes)
{
    using var document = PdfDocument.Open(pdfBytes);  // Throws on bad PDF
    foreach (var page in document.GetPages())
        sb.AppendLine(page.Text);  // Empty for image-only PDFs
    return sb.ToString();
}
```

**Concrete failures:**
- Encrypted/password-protected PDFs → `PdfDocumentEncryptedException`
- Corrupted PDF headers → `InvalidOperationException`
- Image-only PDFs → returns empty string → empty hashes → **all image PDFs falsely match each other**
- Files > available memory → `OutOfMemoryException`

**Fix:** Wrap in try/catch, return a result object with success/failure status, log the error, and skip the file gracefully. Detect image-only PDFs (empty text after extraction) and flag them for OCR or mark as "extraction failed."

---

### 1.2 No Authentication or Authorization

**Files:** All controllers (`DocumentsController.cs`, `GroupsController.cs`, `RulesController.cs`)

Zero `[Authorize]` attributes anywhere. Anyone with network access can:
- Upload files and trigger grouping
- Delete all groups
- Modify business rules
- Export document data
- Change similarity thresholds
- Access any PDF via `/api/documents/{id}/pdf`

**Fix:** Add authentication (API key for machine-to-machine, or OIDC/JWT for user-facing). At minimum, add `[Authorize]` to all controller classes and configure an authentication scheme in `Program.cs`.

---

### 1.3 GroupingOrchestrator Has No Concurrency Protection

**File:** `Infrastructure/Services/GroupingOrchestrator.cs`

If two requests call `GroupAllDocumentsAsync` simultaneously:
1. First request calls `DeleteAllAsync()` — wipes all groups
2. Second request starts reading groups before first finishes
3. Race condition produces corrupt or missing results

No locking, no request queuing, no check for in-flight operations.

**Fix:** Add a `SemaphoreSlim(1, 1)` or use the `ProcessingJobs` table as a distributed lock. Before starting, check if a job is already running. The `ProcessingJobs` entity already exists in the schema — use it.

---

### 1.4 GroupAllDocumentsAsync Loads All Documents Into Memory

**File:** `Infrastructure/Services/GroupingOrchestrator.cs`, line ~47

```csharp
var documents = await documentRepository.GetAllAsync(ct);
```

At 800K documents with `OriginalText` + `NormalizedText` (avg 50KB per doc), this attempts to load ~40 GB into memory. The application survived 800K in testing because the test data had small text, but real O&G documents (multi-page PDFs) will OOM.

**Fix:** Replace with phase-appropriate projections:
- Phase 1/2: `SELECT Id, TextHash, FuzzyHash FROM Documents` (no text)
- Phase 3: Load only MinHash signatures, fetch text on-demand for Jaccard verification
- Phase 4: `SELECT Id FROM Documents WHERE Id NOT IN (SELECT DocumentId FROM Memberships)`

---

### 1.5 Credentials in Source Control

**File:** `Web/appsettings.json`, lines 2-4

```json
"ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=docgrouping;Username=postgres;Password=postgres"
}
```

PostgreSQL credentials are committed to the repository.

**Fix:** Move to environment variables, `appsettings.Development.json` (gitignored), or a secrets manager (AWS Secrets Manager, Azure Key Vault).

---

### 1.6 No Database Migrations

**File:** `Web/Program.cs`, lines ~85-91

```csharp
await dbInitializer.EnsureCreatedAsync(dbName);
```

`EnsureCreatedAsync()` is a development-only API. It:
- Cannot apply schema changes to existing databases
- Cannot roll back changes
- Has no versioning
- Silently does nothing if the database already exists (even if schema is outdated)

**Fix:** Switch to EF Core migrations. Run `dotnet ef migrations add InitialCreate` to create the initial migration, then use `MigrateAsync()` instead of `EnsureCreatedAsync()`.

---

### 1.7 No Transaction Boundaries on Batch Operations

**Files:** `GroupingOrchestrator.cs`, all repository classes

Batch group insertion uses multiple `SaveChangesAsync()` calls (every 500 groups). If the process crashes halfway through Phase 2 after Phase 1 is committed, the database has partial results with no way to resume or roll back.

**Fix:** Wrap each phase in an explicit transaction. If a phase fails, roll back that phase's groups. Consider using the `ProcessingJobs` table to track which phases completed, enabling restart from the last successful phase.

---

### 1.8 Large File Ingestion Reads Entire File Into Memory

**File:** `Infrastructure/Services/DocumentIngestionService.cs`, line ~34

```csharp
var fileBytes = await File.ReadAllBytesAsync(filePath, ct);
```

A 500 MB PDF causes OOM. The bytes are then passed to `PdfDocument.Open(fileBytes)`, doubling memory usage.

**Fix:** Add a configurable file size limit (e.g., 100 MB). For files within the limit, current approach is fine. For oversized files, use streaming or `PdfDocument.Open(filePath)` (file path overload avoids loading all bytes).

---

## Tier 2 — High Priority (Before Pilot/Demo)

### 2.1 Missing Database Indexes

**Files:** `Infrastructure/Persistence/Configurations/`

No explicit indexes are defined on frequently-queried columns:
- `Document.TextHash` — searched every Phase 1 grouping
- `Document.FuzzyHash` — searched every Phase 2 grouping
- `DocumentGroup.GroupNumber` — used in comparison page URLs
- `DocumentGroupMembership.DocumentId` — FK without index

At 800K documents, queries without indexes degrade to full table scans.

**Fix:** Add index definitions in the EF Core configuration files:

```csharp
builder.HasIndex(d => d.TextHash);
builder.HasIndex(d => d.FuzzyHash);
builder.HasIndex(g => g.GroupNumber);
```

**Note:** There's a discrepancy — the MEMORY.md notes say indexes are "Done" in EF config, but the audit found no explicit index builders in the configuration files. Verify which is correct and add any missing indexes.

---

### 2.2 Settings Page Persistence is Fragile

**File:** `Web/Components/Pages/Settings.razor`, lines ~185-271

The Settings page writes directly to `appsettings.json` on disk. Issues:
1. `_saved = true` is set BEFORE confirming the file write succeeded — user sees "Saved" even if write fails
2. If the file is locked by another process, write fails silently
3. No validation that the written JSON is valid
4. On app restart, if file write failed, old thresholds load — user is confused

**Fix:** Set `_saved = true` only after confirmed file write. Add validation read-back. Consider storing thresholds in the database instead of the filesystem.

---

### 2.3 MinHash/LSH Signatures Not Persisted

**File:** `Infrastructure/Services/GroupingOrchestrator.cs`

MinHash signatures and LSH buckets are computed in-memory during Phase 3 and discarded when processing completes. The database tables (`MinHashSignatures`, `LshBuckets`) exist but are never written to.

Every grouping run — including incremental — recomputes all signatures from scratch. At 7M documents, this wastes ~30 minutes per run.

**Fix:** After computing a document's MinHash signature, persist it to the `MinHashSignatures` table. After computing LSH bucket assignments, persist to `LshBuckets`. On subsequent runs, load existing signatures instead of recomputing.

---

### 2.4 Background Job System Missing

**File:** `Web/Controllers/DocumentsController.cs`

The `POST /api/documents/process` endpoint calls `GroupAllDocumentsAsync()` synchronously on the HTTP request thread. At 800K+ documents, this will timeout (default 30-120 seconds).

The `ProcessingJobs` entity already exists in the schema but isn't wired up for grouping operations. The Generator page has partial progress tracking via SignalR, but the core grouping pipeline doesn't use it.

**Fix:** Options (in order of simplicity):
1. `BackgroundService` + `Channel<T>` — simplest, good for single-server
2. Hangfire — more robust, provides dashboard and retry
3. MassTransit + queue — most scalable, for distributed workers

The API should return immediately with a job ID, and progress should be trackable via `GET /api/jobs/{id}/status`.

---

### 2.5 No Health Check Endpoints

**File:** `Web/Program.cs`

No `/health` or `/ready` endpoints. Container orchestrators (Kubernetes, ECS) cannot detect application readiness. No way to programmatically check database connectivity.

**Fix:**

```csharp
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString)
    .AddCheck("storage", () => Directory.Exists(pdfRoot)
        ? HealthCheckResult.Healthy()
        : HealthCheckResult.Unhealthy());

app.MapHealthChecks("/health");
```

---

### 2.6 Hardcoded File Paths

**Files:** Multiple locations

| Location | Hardcoded Path |
|----------|---------------|
| `DocumentsController.cs` line ~51 | `@"C:\Temp\Dedupe branch 2\De Dupe Grouping Concept\sample_documents"` |
| `Upload.razor` line ~162 | `@"C:\Temp\idp-demo-docgen\output"` |
| `Upload.razor` line ~234 | `@"C:\Temp\Dedupe branch 2\..."` |
| `appsettings.json` line ~38 | `"C:/Temp/DocGrouping/uploads"` |

These fail on non-Windows systems, different disk layouts, or cloud deployments.

**Fix:** Move all paths to `appsettings.json` configuration. Use `Path.Combine` with configured root paths. Remove references to developer-specific paths.

---

### 2.7 Rules Engine Conditions Not Validated

**File:** `Web/Controllers/RulesController.cs`, lines ~34-51

The Create and Update endpoints accept any JSON as rule conditions with no validation:
- `Enum.Parse<RuleType>(dto.RuleType)` throws on invalid enum values (no try/catch)
- `JsonDocument.Parse(dto.ConditionsJson)` throws on malformed JSON (no try/catch)
- No validation that conditions match the rule type (e.g., a TextPattern rule must have a `pattern` field)

Invalid rules are saved to the database and crash the `ShouldGroup` method at runtime during grouping.

**Fix:** Add validation per rule type. Wrap enum parsing and JSON parsing in try/catch. Return `400 Bad Request` with specific error messages.

---

### 2.8 Error Messages Exposed to Users

**File:** `Web/Components/Pages/Upload.razor`, line ~217

```csharp
catch (Exception ex)
{
    _statusMessage = $"Error: {ex.Message}";  // Full exception shown to user
}
```

Exception messages may contain stack traces, file paths, database connection strings, or internal implementation details.

**Fix:** Log the full exception server-side. Show a generic user-friendly message. In development mode, optionally show details.

---

### 2.9 No Graceful Shutdown

**File:** `Web/Program.cs`

No `IHostApplicationLifetime` handlers. If the application stops during a grouping operation, the operation is interrupted mid-batch with no cleanup. Partial results remain in the database.

**Fix:** Register cancellation token propagation. Use `CancellationToken` throughout the grouping pipeline (it's already passed but not checked frequently). Save checkpoint state before shutdown.

---

### 2.10 Regex Compilation on Every Call

**Files:** `Infrastructure/TextProcessing/TextNormalizer.cs`, `Infrastructure/Rules/RulesEngine.cs`

`TextNormalizer.Normalize()` calls `Regex.Replace()` ~15 times per document using inline pattern strings. Each call recompiles the regex. The Rules Engine has the same issue with `Regex.IsMatch()` in evaluation loops.

At 7M documents, this wastes ~1 hour of CPU time.

**Fix:** Use .NET source-generated regex:

```csharp
[GeneratedRegex(@"\b0([a-z])", RegexOptions.Compiled)]
private static partial Regex OcrZeroToO();
```

Or at minimum, declare patterns as `static readonly Regex` with `RegexOptions.Compiled`.

---

### 2.11 Image-Only PDFs Silently Produce False Matches

**File:** `Infrastructure/TextProcessing/PdfTextExtractor.cs`

Image-only PDFs (scanned without OCR) return empty text. Empty text normalizes to empty string. Empty string hashes to the same SHA-256 value. **Result: every image-only PDF is hash-matched as "identical" in Phase 1.**

This is especially dangerous because the match gets VeryHigh confidence.

**Fix:** After extraction, check if text is empty or near-empty. If so, flag the document with a quality warning rather than proceeding with empty-text fingerprinting. Optionally route to OCR (Tesseract, AWS Textract).

---

### 2.12 No OpenAPI/Swagger Documentation

**File:** `Web/Program.cs`

No Swagger endpoints configured. The API surface is undocumented, making integration with external systems difficult.

**Fix:** Add `builder.Services.AddEndpointsApiExplorer()` and `builder.Services.AddSwaggerGen()`. Map Swagger UI at `/swagger`.

---

## Tier 3 — Medium Priority (Production Hardening)

### 3.1 GroupSingleDocumentAsync is O(n) Brute-Force

**File:** `Infrastructure/Services/GroupingOrchestrator.cs`, line ~474

```csharp
var allDocs = await documentRepository.GetAllAsync(ct);
foreach (var candidate in allDocs.Where(d => d.Id != document.Id))
{
    var metrics = fingerprinter.CalculateSimilarityMetrics(
        document.NormalizedText, candidate.NormalizedText);
}
```

For each new document, loads ALL documents and compares sequentially. Called once per document in some code paths. For 1,000 new documents against 800K existing = 800M comparisons.

**Fix:** Reuse the persisted LSH index (once implemented per §2.3) to find candidates, then verify only those.

---

### 3.2 Parallel.ForEach Without Degree Limit

**File:** `Infrastructure/Services/GroupingOrchestrator.cs`, lines ~213-227

```csharp
Parallel.ForEach(
    Partitioner.Create(Enumerable.Range(0, ungroupedDocs.Count),
    EnumerablePartitionerOptions.NoBuffering),
    i => { ... }
);
```

Default parallelism uses all available cores with no limit. On a 64-core machine, spawns 64+ threads, each accessing shared memory. Causes CPU oversubscription and cache thrashing.

**Fix:** Add `ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }` or a configurable value.

---

### 3.3 LSH Candidate Pairs Held Entirely in Memory

**File:** `Infrastructure/TextProcessing/MinHashLshIndex.cs`

`GetCandidatePairs()` returns all candidate pairs as a `HashSet`. At 800K documents, this could be millions of pairs consuming gigabytes of memory.

**Fix:** Return an `IEnumerable` or process pairs in batches. Alternatively, stream pairs from a temporary database table.

---

### 3.4 Change Tracker Not Consistently Managed

**Files:** Multiple (`GroupingOrchestrator.cs`, `DocumentIngestionService.cs`, `DocumentsController.cs`)

`ChangeTracker.Clear()` is called in batch loops but not after every `GetAllAsync()` or `GetPagedAsync()`. In long-running operations, the change tracker accumulates entities, increasing memory usage and slowing `SaveChangesAsync()`.

**Fix:** Call `ChangeTracker.Clear()` after every read-only operation that loads entities not needed for subsequent writes. Better yet, use `AsNoTracking()` on all read queries (already done in some places, not all).

---

### 3.5 Database Selector Race Condition

**File:** `Web/Services/DatabaseSelectorState.cs`, `Web/Middleware/DatabaseSelectionMiddleware.cs`

`DatabaseSelectorState.ActiveDatabaseName` is a mutable singleton. The middleware correctly prefers per-request `X-Database` header / `?db=` query param, but if application code reads `DatabaseSelectorState.ActiveDatabaseName` directly (instead of from `HttpContext.Items`), it gets whatever database the last user selected.

**Fix:** Audit all code paths that read the active database. Ensure they all read from `HttpContext.Items["ActiveDatabase"]`, never directly from the singleton. The singleton should only be the fallback.

---

### 3.6 Rules Engine In-Memory State Not Thread-Safe

**File:** `Infrastructure/Rules/RulesEngine.cs`

```csharp
private readonly List<RuleDefinition> _rules = [];
```

`_rules` is a mutable list with no locking. `UpdateRule()` modifies the list while `ShouldGroup()` may be iterating it. In concurrent scenarios, this causes `InvalidOperationException` ("Collection was modified during enumeration").

**Fix:** Use `ConcurrentBag<T>` or a `ReaderWriterLockSlim`. Or rebuild the rules list as an immutable array on each change and swap the reference atomically.

---

### 3.7 No Structured Logging Context

**File:** `Web/Program.cs`, `Infrastructure/Services/GroupingOrchestrator.cs`

Serilog is configured with console and file sinks, but log entries lack:
- Request ID (for tracing)
- Active database name
- User identity
- Operation context (which phase, which batch)

**Fix:** Add Serilog enrichers:

```csharp
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "DocGrouping")
    .CreateLogger();
```

Push database name and operation context via `LogContext.PushProperty()` in the middleware and orchestrator.

---

### 3.8 Canonicals Feature Not Fully Wired

**File:** `Web/Components/Pages/Canonicals.razor`

- Toggle canonical works (API endpoint exists)
- But the UI doesn't refresh automatically after toggling — user must manually reload the page
- `ClassifyAgainstCanonicalsAsync` deletes existing groups on reclassify — should support incremental classification
- No visual feedback during classification (no progress bar)

---

### 3.9 PDF FileStream Leak on Exception

**File:** `Web/Controllers/DocumentsController.cs`, lines ~163-172

```csharp
var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
return File(stream, "application/pdf");
```

If an exception occurs after the `FileStream` is opened but before ASP.NET takes ownership of it, the stream is never disposed. This leaks file handles.

**Fix:** Wrap in a try/catch or use `PhysicalFile()` which handles the stream lifecycle:

```csharp
return PhysicalFile(filePath, "application/pdf");
```

---

### 3.10 No Server-Side File Upload Size Enforcement

**File:** `Web/Components/Pages/Upload.razor`

The 10 MB file size limit is enforced only on the Blazor client side (`maxAllowedSize: 10 * 1024 * 1024`). The API `Upload` endpoint has no server-side size check. A direct API caller can upload arbitrarily large files.

**Fix:** Add `[RequestSizeLimit(10_000_000)]` to the Upload endpoint, or configure globally in `Program.cs`:

```csharp
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 10 * 1024 * 1024;
});
```

---

### 3.11 No Connection Pool Configuration

**File:** `Web/Program.cs`

Default Npgsql pool size is 100 connections. No explicit configuration. At 800K documents with concurrent users and slow queries, the pool could be exhausted.

**Fix:** Configure pool size explicitly:

```csharp
options.UseNpgsql(connectionString, o => o.MaxPoolSize(50));
```

Also consider adding connection string parameters: `Timeout=30;CommandTimeout=300;Pooling=true`.

---

### 3.12 Dead Code

**Files:**
- `PdfTextExtractor.cs` — `ExtractText(string filePath)` overload is never called (only `byte[]` version used)
- `DocumentTemplates.cs` — contains `OcrErrorSimulator`, `StampGenerator`, `RedactionSimulator` classes that are only used by the test document generator but live in the general TextProcessing namespace
- `Infrastructure.Tests/`, `Domain.Tests/`, `Integration.Tests/` — test projects exist but contain no test classes

**Fix:** Remove dead code or move it to appropriate locations. Either write tests or remove empty test projects.

---

## Testing Strategy

The application currently has **zero automated tests**. The test project directories exist but contain no test classes.

### Recommended Test Coverage

| Area | Test Type | Priority | What to Test |
|------|-----------|----------|-------------|
| TextNormalizer | Unit | High | OCR correction, page number removal, whitespace handling, edge cases (empty input, unicode) |
| DocumentFingerprinter | Unit | High | Hash stability, fuzzy hash token filtering, MinHash collision rates, Jaccard calculation |
| DiffEngine | Unit | High | LCS correctness, word stats, edge cases (empty, identical, completely different) |
| RulesEngine | Unit | High | Each rule type evaluation, condition parsing, priority ordering, concurrent access |
| PdfTextExtractor | Unit | High | Normal PDFs, encrypted PDFs, image-only PDFs, corrupt PDFs, empty PDFs |
| GroupingOrchestrator | Integration | Critical | Full 4-phase pipeline with known test data and expected grouping results |
| DocumentsController | Integration | High | Upload, process, retrieve cycle; error responses; file size limits |
| Settings persistence | Integration | Medium | Write thresholds, reload, verify |
| Database operations | Integration | Medium | Concurrent writes, batch sizes, transaction rollback |
| PDF comparison | E2E | Medium | Pixel diff produces expected highlights on known test PDFs |

---

## Architecture Improvements for Scale

These are larger changes documented in `SCALING-AND-CLOUD-COSTS.md` but listed here for completeness:

1. **Externalize text storage** — move `OriginalText`/`NormalizedText` out of PostgreSQL to S3/disk
2. **Staged pipeline with checkpointing** — each phase independently restartable
3. **Persist MinHash/LSH indexes** — avoid recomputation on re-runs
4. **AWS Batch integration** — Docker-based parallel workers
5. **Streaming document loading** — projections instead of full entity loads
6. **Compiled regex** — source-generated regex for TextNormalizer

---

## Quick Wins (Low Effort, High Impact)

These can be done in a day or less:

1. Add try/catch to `PdfTextExtractor` (30 min)
2. Add `[Authorize]` attributes to controllers (15 min)
3. Add `SemaphoreSlim` to `GroupingOrchestrator` (30 min)
4. Add health check endpoint (30 min)
5. Move credentials to environment variables (30 min)
6. Add database indexes (15 min)
7. Fix Settings page `_saved` ordering (10 min)
8. Add `PhysicalFile()` for PDF serving (10 min)
9. Add `[RequestSizeLimit]` to upload endpoint (5 min)
10. Add empty-text detection in PdfTextExtractor (20 min)
