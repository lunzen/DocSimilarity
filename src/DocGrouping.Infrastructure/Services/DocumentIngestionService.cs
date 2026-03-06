using System.Security.Cryptography;
using DocGrouping.Application.Interfaces;
using DocGrouping.Domain.Entities;
using DocGrouping.Domain.Interfaces;
using DocGrouping.Infrastructure.Persistence;
using DocGrouping.Infrastructure.TextProcessing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DocGrouping.Infrastructure.Services;

public class DocumentIngestionService(
	IDocumentRepository documentRepository,
	DocGroupingDbContext dbContext,
	TextNormalizer normalizer,
	DocumentFingerprinter fingerprinter,
	PdfTextExtractor pdfExtractor,
	IPdfStorageService pdfStorage,
	IHttpContextAccessor httpContextAccessor,
	ILogger<DocumentIngestionService> logger) : IDocumentIngestionService
{
	private string GetActiveDatabase()
	{
		return httpContextAccessor.HttpContext?.Items["ActiveDatabase"] as string ?? "docgrouping";
	}

	public async Task<Document> IngestFileAsync(string filePath, CancellationToken ct = default)
	{
		var fileName = Path.GetFileName(filePath);
		var extension = Path.GetExtension(filePath).ToLowerInvariant();

		logger.LogInformation("Ingesting file: {FileName}", fileName);

		var fileBytes = await File.ReadAllBytesAsync(filePath, ct);
		var fileHash = fingerprinter.GenerateFileHash(fileBytes);

		string originalText;
		if (extension == ".pdf")
		{
			originalText = pdfExtractor.ExtractText(fileBytes);
		}
		else
		{
			originalText = System.Text.Encoding.UTF8.GetString(fileBytes);
		}

		// PostgreSQL rejects null bytes in text columns
		originalText = originalText.Replace("\0", "");
		var normalizedText = normalizer.Normalize(originalText);
		var (textHash, fuzzyHash) = fingerprinter.GenerateAllFingerprints(normalizedText);
		var tokens = normalizer.GetTokens(normalizedText);

		var document = new Document
		{
			FileName = fileName,
			FilePath = filePath,
			FileSizeBytes = fileBytes.Length,
			FileHash = fileHash,
			OriginalText = originalText,
			NormalizedText = normalizedText,
			TextHash = textHash,
			FuzzyHash = fuzzyHash,
			WordCount = tokens.Count,
			SourceFolder = Path.GetDirectoryName(filePath)
		};

		await documentRepository.AddAsync(document, ct);

		if (extension == ".pdf")
			await pdfStorage.SaveAsync(document.Id, GetActiveDatabase(), fileBytes);

		logger.LogInformation("Ingested document {FileName}: {WordCount} words, text_hash={TextHash}",
			fileName, tokens.Count, textHash[..16]);

		return document;
	}

	public async Task<Document> IngestBytesAsync(string fileName, byte[] fileBytes, CancellationToken ct = default)
	{
		var extension = Path.GetExtension(fileName).ToLowerInvariant();
		var fileHash = fingerprinter.GenerateFileHash(fileBytes);

		string originalText;
		if (extension == ".pdf")
		{
			originalText = pdfExtractor.ExtractText(fileBytes);
		}
		else
		{
			originalText = System.Text.Encoding.UTF8.GetString(fileBytes);
		}

		// PostgreSQL rejects null bytes in text columns
		originalText = originalText.Replace("\0", "");
		var normalizedText = normalizer.Normalize(originalText);
		var (textHash, fuzzyHash) = fingerprinter.GenerateAllFingerprints(normalizedText);
		var tokens = normalizer.GetTokens(normalizedText);

		var document = new Document
		{
			FileName = fileName,
			FilePath = string.Empty,
			FileSizeBytes = fileBytes.Length,
			FileHash = fileHash,
			OriginalText = originalText,
			NormalizedText = normalizedText,
			TextHash = textHash,
			FuzzyHash = fuzzyHash,
			WordCount = tokens.Count
		};

		await documentRepository.AddAsync(document, ct);

		if (extension == ".pdf")
			await pdfStorage.SaveAsync(document.Id, GetActiveDatabase(), fileBytes);

		return document;
	}

	public async Task<Document> IngestTextAsync(string fileName, string text, string? documentType = null, CancellationToken ct = default)
	{
		// PostgreSQL rejects null bytes in text columns
		text = text.Replace("\0", "");
		var normalizedText = normalizer.Normalize(text);
		var (textHash, fuzzyHash) = fingerprinter.GenerateAllFingerprints(normalizedText);
		var tokens = normalizer.GetTokens(normalizedText);
		var fileHash = fingerprinter.GenerateFileHash(System.Text.Encoding.UTF8.GetBytes(text));

		var document = new Document
		{
			FileName = fileName,
			FilePath = string.Empty,
			FileSizeBytes = System.Text.Encoding.UTF8.GetByteCount(text),
			FileHash = fileHash,
			OriginalText = text,
			NormalizedText = normalizedText,
			TextHash = textHash,
			FuzzyHash = fuzzyHash,
			WordCount = tokens.Count,
			DocumentType = documentType
		};

		await documentRepository.AddAsync(document, ct);
		return document;
	}

	public async Task<List<Document>> IngestTextBatchAsync(
		IReadOnlyList<(string FileName, string Text, string? DocumentType)> items,
		int batchSize = 500,
		IProgress<(int processed, int total)>? progress = null,
		CancellationToken ct = default)
	{
		logger.LogInformation("Batch ingesting {Count} documents (batch size {BatchSize})", items.Count, batchSize);
		var allDocuments = new List<Document>(items.Count);
		var batch = new List<Document>(batchSize);

		for (var i = 0; i < items.Count; i++)
		{
			ct.ThrowIfCancellationRequested();
			var (fileName, text, documentType) = items[i];

			var normalizedText = normalizer.Normalize(text);
			var (textHash, fuzzyHash) = fingerprinter.GenerateAllFingerprints(normalizedText);
			var tokens = normalizer.GetTokens(normalizedText);
			var fileHash = fingerprinter.GenerateFileHash(System.Text.Encoding.UTF8.GetBytes(text));

			batch.Add(new Document
			{
				FileName = fileName,
				FilePath = string.Empty,
				FileSizeBytes = System.Text.Encoding.UTF8.GetByteCount(text),
				FileHash = fileHash,
				OriginalText = text,
				NormalizedText = normalizedText,
				TextHash = textHash,
				FuzzyHash = fuzzyHash,
				WordCount = tokens.Count,
				DocumentType = documentType
			});

			if (batch.Count >= batchSize)
			{
				await documentRepository.AddRangeAsync(batch, ct);
				allDocuments.AddRange(batch);
				dbContext.ChangeTracker.Clear();
				batch.Clear();
				progress?.Report((i + 1, items.Count));
				logger.LogInformation("Ingested {Count}/{Total} documents", i + 1, items.Count);
			}
		}

		if (batch.Count > 0)
		{
			await documentRepository.AddRangeAsync(batch, ct);
			allDocuments.AddRange(batch);
			dbContext.ChangeTracker.Clear();
			progress?.Report((items.Count, items.Count));
			logger.LogInformation("Ingested {Count}/{Total} documents", items.Count, items.Count);
		}

		return allDocuments;
	}

	public async Task<List<Document>> IngestDirectoryAsync(string directoryPath, CancellationToken ct = default)
	{
		var files = Directory.GetFiles(directoryPath)
			.Where(f =>
			{
				var ext = Path.GetExtension(f).ToLowerInvariant();
				return ext is ".txt" or ".pdf";
			})
			.OrderBy(f => f)
			.ToList();

		logger.LogInformation("Found {Count} files in {Directory}", files.Count, directoryPath);

		var documents = new List<Document>();
		foreach (var file in files)
		{
			var doc = await IngestFileAsync(file, ct);
			documents.Add(doc);
		}

		return documents;
	}
}
