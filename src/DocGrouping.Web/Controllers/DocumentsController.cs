using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DocGrouping.Application.DTOs;
using DocGrouping.Application.Interfaces;
using DocGrouping.Domain.Entities;
using DocGrouping.Domain.Interfaces;
using DocGrouping.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocGrouping.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DocumentsController(
	IDocumentIngestionService ingestionService,
	IDocumentRepository documentRepository,
	IGroupingOrchestrator groupingOrchestrator,
	IDocumentGeneratorService generatorService,
	IPdfStorageService pdfStorage,
	DocGroupingDbContext dbContext) : ControllerBase
{
	[HttpPost("upload")]
	public async Task<IActionResult> Upload(IFormFileCollection files, CancellationToken ct)
	{
		var uploadDir = Path.Combine(Path.GetTempPath(), "docgrouping", "uploads", Guid.NewGuid().ToString());
		Directory.CreateDirectory(uploadDir);

		var documents = new List<object>();
		foreach (var file in files)
		{
			if (file.Length == 0) continue;
			var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
			if (ext is not ".txt" and not ".pdf") continue;

			var filePath = Path.Combine(uploadDir, file.FileName);
			using (var stream = new FileStream(filePath, FileMode.Create))
			{
				await file.CopyToAsync(stream, ct);
			}

			var doc = await ingestionService.IngestFileAsync(filePath, ct);
			documents.Add(new { doc.Id, doc.FileName, doc.WordCount });
		}

		return Ok(new { uploaded_count = documents.Count, documents });
	}

	[HttpPost("load-samples")]
	public async Task<IActionResult> LoadSamples([FromBody] LoadSamplesRequest? request, CancellationToken ct)
	{
		var sampleDir = request?.SampleDirectory
			?? @"C:\Temp\Dedupe branch 2\De Dupe Grouping Concept\sample_documents";

		if (!Directory.Exists(sampleDir))
			return NotFound(new { error = $"Sample directory not found: {sampleDir}" });

		var documents = await ingestionService.IngestDirectoryAsync(sampleDir, ct);

		return Ok(new
		{
			loaded_count = documents.Count,
			documents = documents.Select(d => new { d.Id, d.FileName, d.WordCount })
		});
	}

	[HttpPost("process")]
	public async Task<IActionResult> Process(CancellationToken ct)
	{
		var sw = System.Diagnostics.Stopwatch.StartNew();
		var groups = await groupingOrchestrator.GroupAllDocumentsAsync(ct);
		sw.Stop();

		var stats = await groupingOrchestrator.GetStatisticsAsync(ct);

		return Ok(new
		{
			statistics = stats,
			group_count = groups.Count,
			processing_time_seconds = sw.Elapsed.TotalSeconds
		});
	}

	[HttpGet]
	public async Task<IActionResult> GetAll(CancellationToken ct)
	{
		var docs = await documentRepository.GetAllAsync(ct);
		return Ok(docs.Select(d => new
		{
			d.Id,
			d.FileName,
			d.WordCount,
			d.DocumentType,
			d.IsCanonicalReference,
			text_hash = d.TextHash[..16] + "...",
			fuzzy_hash = d.FuzzyHash[..16] + "...",
			d.CreatedAt
		}));
	}

	[HttpGet("export")]
	public async Task<IActionResult> Export(
		[FromQuery] string format = "csv",
		[FromQuery] string labelPrefix = "GRP",
		CancellationToken ct = default)
	{
		var rows = await dbContext.Set<DocumentGroupMembership>()
			.AsNoTracking()
			.Include(m => m.Document)
			.Include(m => m.Group)
			.OrderBy(m => m.Group.GroupNumber)
			.ThenByDescending(m => m.IsCanonical)
			.ThenBy(m => m.Document.FileName)
			.Select(m => new GroupExportRow
			{
				DocumentName = m.Document.FileName,
				DocumentId = m.DocumentId,
				GroupLabel = labelPrefix + "-" + m.Group.GroupNumber.ToString("D5"),
				GroupNumber = m.Group.GroupNumber,
				ConfidenceTier = m.Group.Confidence.ToString(),
				MatchMethod = m.Group.MatchReason,
				SimilarityScore = m.SimilarityScore,
				IsCanonical = m.IsCanonical,
				DocumentType = m.Document.DocumentType,
				GroupSize = m.Group.DocumentCount,
				IngestedAt = m.Document.CreatedAt
			})
			.ToListAsync(ct);

		if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
		{
			var payload = new
			{
				exported_at = DateTimeOffset.UtcNow,
				label_prefix = labelPrefix,
				total_documents = rows.Count,
				total_groups = rows.Select(r => r.GroupNumber).Distinct().Count(),
				documents = rows
			};
			var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions
			{
				WriteIndented = true,
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase
			});
			return File(jsonBytes, "application/json", $"docgrouping-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
		}

		// CSV
		var sb = new StringBuilder();
		sb.AppendLine("DocumentName,DocumentId,GroupLabel,GroupNumber,ConfidenceTier,MatchMethod,SimilarityScore,IsCanonical,DocumentType,GroupSize,IngestedAt");
		foreach (var r in rows)
		{
			sb.Append(CsvEscape(r.DocumentName)).Append(',');
			sb.Append(r.DocumentId).Append(',');
			sb.Append(r.GroupLabel).Append(',');
			sb.Append(r.GroupNumber).Append(',');
			sb.Append(r.ConfidenceTier).Append(',');
			sb.Append(CsvEscape(r.MatchMethod)).Append(',');
			sb.Append(r.SimilarityScore.ToString(CultureInfo.InvariantCulture)).Append(',');
			sb.Append(r.IsCanonical).Append(',');
			sb.Append(CsvEscape(r.DocumentType ?? "")).Append(',');
			sb.Append(r.GroupSize).Append(',');
			sb.AppendLine(r.IngestedAt.ToString("o"));
		}

		var bytes = Encoding.UTF8.GetBytes(sb.ToString());
		return File(bytes, "text/csv", $"docgrouping-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
	}

	private static string CsvEscape(string value)
	{
		if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
			return "\"" + value.Replace("\"", "\"\"") + "\"";
		return value;
	}

	[HttpGet("canonicals")]
	public async Task<IActionResult> GetCanonicals(CancellationToken ct)
	{
		var canonicals = await documentRepository.GetAllCanonicalsAsync(ct);
		var grouped = canonicals
			.GroupBy(d => d.DocumentType ?? "(none)")
			.OrderBy(g => g.Key)
			.Select(g => new
			{
				DocumentType = g.Key,
				Documents = g.Select(d => new
				{
					d.Id,
					d.FileName,
					d.WordCount,
					d.DocumentType,
					d.CreatedAt
				})
			});

		return Ok(grouped);
	}

	[HttpPost("{id:guid}/set-canonical")]
	public async Task<IActionResult> SetCanonical(Guid id, [FromBody] SetCanonicalRequest request, CancellationToken ct)
	{
		var doc = await documentRepository.GetByIdAsync(id, ct);
		if (doc == null) return NotFound();

		doc.IsCanonicalReference = request.IsCanonical;
		await documentRepository.UpdateAsync(doc, ct);

		return Ok(new { doc.Id, doc.FileName, doc.IsCanonicalReference });
	}

	[HttpPost("classify-against-canonicals")]
	public async Task<IActionResult> ClassifyAgainstCanonicals([FromBody] ClassifyRequest? request, CancellationToken ct)
	{
		var sw = System.Diagnostics.Stopwatch.StartNew();
		var results = await groupingOrchestrator.ClassifyAgainstCanonicalsAsync(
			request?.DocumentIds, ct: ct);
		sw.Stop();

		var matched = results.Count(r => r.MatchedCanonicalId.HasValue);
		return Ok(new
		{
			total = results.Count,
			matched,
			unmatched = results.Count - matched,
			processing_time_seconds = sw.Elapsed.TotalSeconds,
			results = results.Select(r => new
			{
				r.DocumentId,
				r.FileName,
				r.DocumentType,
				r.MatchedCanonicalId,
				r.MatchedCanonicalFileName,
				Confidence = r.Confidence.ToString(),
				r.SimilarityScore,
				r.MatchReason
			})
		});
	}

	[HttpGet("{id:guid}/pdf")]
	public IActionResult GetPdf(Guid id)
	{
		var dbName = HttpContext.Items["ActiveDatabase"] as string ?? "docgrouping";
		if (!pdfStorage.Exists(id, dbName))
			return NotFound();

		var filePath = pdfStorage.GetFilePath(id, dbName);
		var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
		return File(stream, "application/pdf");
	}

	[HttpGet("{id:guid}")]
	public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
	{
		var doc = await documentRepository.GetByIdAsync(id, ct);
		if (doc == null) return NotFound();

		return Ok(new
		{
			doc.Id,
			doc.FileName,
			doc.OriginalText,
			doc.NormalizedText,
			doc.WordCount,
			doc.TextHash,
			doc.FuzzyHash,
			doc.FileHash,
			doc.FileSizeBytes,
			doc.DocumentType,
			doc.CreatedAt
		});
	}
	[HttpPost("generate-bulk")]
	public async Task<IActionResult> GenerateBulk([FromBody] BulkGenerateRequest request, CancellationToken ct)
	{
		var sw = System.Diagnostics.Stopwatch.StartNew();

		// Generate
		var generated = generatorService.GenerateBulkDocuments(request.Count);
		var genTime = sw.Elapsed.TotalSeconds;

		// Batch ingest
		var ingestStart = sw.Elapsed;
		var items = generated
			.Select(g => (g.FileName, g.Content, (string?)g.DocumentType))
			.ToList();
		var docs = await ingestionService.IngestTextBatchAsync(items, batchSize: 500, ct: ct);
		var ingestTime = (sw.Elapsed - ingestStart).TotalSeconds;

		// Mark canonical if requested — batch update
		if (request.MarkCanonical)
		{
			var canonicalBatch = new List<Document>();
			foreach (var doc in docs.Where(d => !d.IsCanonicalReference))
			{
				doc.IsCanonicalReference = true;
				canonicalBatch.Add(doc);
			}
			if (canonicalBatch.Count > 0)
				await documentRepository.UpdateRangeAsync(canonicalBatch, ct);
		}

		dbContext.ChangeTracker.Clear();

		// Group
		var groupStart = sw.Elapsed;
		Application.Interfaces.GroupingMetrics? metrics = null;
		var progress = new Progress<Application.Interfaces.GroupingProgress>(p =>
		{
			if (p.Metrics is not null) metrics = p.Metrics;
		});

		if (request.Mode == "incremental")
			await groupingOrchestrator.GroupIncrementalAsync(progress, ct);
		else
			await groupingOrchestrator.GroupAllDocumentsAsync(progress, ct);

		var groupTime = (sw.Elapsed - groupStart).TotalSeconds;
		sw.Stop();

		dbContext.ChangeTracker.Clear();
		var stats = await groupingOrchestrator.GetStatisticsAsync(ct);

		return Ok(new
		{
			generated = generated.Count,
			mark_canonical = request.MarkCanonical,
			mode = request.Mode,
			generation_seconds = genTime,
			ingestion_seconds = ingestTime,
			grouping_seconds = groupTime,
			total_seconds = sw.Elapsed.TotalSeconds,
			statistics = stats,
			metrics
		});
	}

	[HttpPost("import-duplicate-sets")]
	public async Task<IActionResult> ImportDuplicateSets([FromBody] ImportDuplicateSetsRequest request, CancellationToken ct)
	{
		var sourceDir = request.SourceDirectory ?? @"C:\Temp\idp-demo-docgen\output";

		if (!Directory.Exists(sourceDir))
			return NotFound(new { error = $"Directory not found: {sourceDir}" });

		var manifestFiles = Directory.GetFiles(sourceDir, "*_manifest.json");
		if (manifestFiles.Length == 0)
			return BadRequest(new { error = "No manifest files found in directory" });

		var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
		var importedDocs = new List<object>();
		var setsImported = 0;

		foreach (var manifestPath in manifestFiles)
		{
			var json = await System.IO.File.ReadAllTextAsync(manifestPath, ct);
			var manifest = JsonSerializer.Deserialize<DuplicateSetManifest>(json, jsonOptions);
			if (manifest == null) continue;

			foreach (var member in manifest.Members)
			{
				var pdfPath = Path.Combine(sourceDir, member.PdfFile);
				if (!System.IO.File.Exists(pdfPath)) continue;

				var doc = await ingestionService.IngestFileAsync(pdfPath, ct);

				doc.DocumentType = manifest.DocType;
				doc.CustomMetadata = JsonDocument.Parse(JsonSerializer.Serialize(new
				{
					duplicate_set_id = manifest.SetId,
					expected_similarity = member.Similarity,
					company_name = manifest.Company?.Name,
					company_id = manifest.Company?.Id,
					scan_profile = member.ScanProfile,
					description = member.Description
				}));

				await documentRepository.UpdateAsync(doc, ct);

				importedDocs.Add(new
				{
					doc.Id,
					doc.FileName,
					doc.DocumentType,
					duplicate_set_id = manifest.SetId,
					expected_similarity = member.Similarity
				});
			}

			setsImported++;
		}

		return Ok(new
		{
			sets_imported = setsImported,
			documents_imported = importedDocs.Count,
			documents = importedDocs
		});
	}

	[HttpPost("process-incremental")]
	public async Task<IActionResult> ProcessIncremental(CancellationToken ct)
	{
		var sw = System.Diagnostics.Stopwatch.StartNew();
		Application.Interfaces.GroupingMetrics? metrics = null;
		var progress = new Progress<Application.Interfaces.GroupingProgress>(p =>
		{
			if (p.Metrics is not null) metrics = p.Metrics;
		});

		var groups = await groupingOrchestrator.GroupIncrementalAsync(progress, ct);
		sw.Stop();

		var stats = await groupingOrchestrator.GetStatisticsAsync(ct);

		return Ok(new
		{
			statistics = stats,
			group_count = groups.Count,
			processing_time_seconds = sw.Elapsed.TotalSeconds,
			metrics
		});
	}
}

public class BulkGenerateRequest
{
	public int Count { get; set; } = 100;
	public bool MarkCanonical { get; set; }
	public string Mode { get; set; } = "full";
}

public class LoadSamplesRequest
{
	public string? SampleDirectory { get; set; }
}

public class SetCanonicalRequest
{
	public bool IsCanonical { get; set; }
}

public class ClassifyRequest
{
	public List<Guid>? DocumentIds { get; set; }
}

public class ImportDuplicateSetsRequest
{
	[JsonPropertyName("source_directory")]
	public string? SourceDirectory { get; set; }
}

public class DuplicateSetManifest
{
	[JsonPropertyName("set_id")]
	public string SetId { get; set; } = "";

	[JsonPropertyName("doc_type")]
	public string DocType { get; set; } = "";

	[JsonPropertyName("company")]
	public ManifestCompany? Company { get; set; }

	[JsonPropertyName("members")]
	public List<ManifestMember> Members { get; set; } = [];
}

public class ManifestCompany
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("name")]
	public string Name { get; set; } = "";
}

public class ManifestMember
{
	[JsonPropertyName("similarity")]
	public string Similarity { get; set; } = "";

	[JsonPropertyName("pdf_file")]
	public string PdfFile { get; set; } = "";

	[JsonPropertyName("description")]
	public string? Description { get; set; }

	[JsonPropertyName("scan_profile")]
	public string? ScanProfile { get; set; }
}
