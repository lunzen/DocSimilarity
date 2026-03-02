using DocGrouping.Application.Interfaces;
using DocGrouping.Domain.Interfaces;
using DocGrouping.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace DocGrouping.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DocumentsController(
	IDocumentIngestionService ingestionService,
	IDocumentRepository documentRepository,
	IGroupingOrchestrator groupingOrchestrator,
	IDocumentGeneratorService generatorService,
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

		// Ingest
		var ingestStart = sw.Elapsed;
		foreach (var doc in generated)
			await ingestionService.IngestTextAsync(doc.FileName, doc.Content, doc.DocumentType, ct);
		var ingestTime = (sw.Elapsed - ingestStart).TotalSeconds;

		// Mark canonical if requested
		if (request.MarkCanonical)
		{
			var allDocs = await documentRepository.GetAllAsync(ct);
			var generatedNames = generated.Select(g => g.FileName).ToHashSet();
			foreach (var doc in allDocs.Where(d => generatedNames.Contains(d.FileName) && !d.IsCanonicalReference))
			{
				doc.IsCanonicalReference = true;
				await documentRepository.UpdateAsync(doc, ct);
			}
		}

		// Clear change tracker so grouping starts with a clean slate
		// (ingestion leaves tracked entities that can cause concurrency issues)
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
