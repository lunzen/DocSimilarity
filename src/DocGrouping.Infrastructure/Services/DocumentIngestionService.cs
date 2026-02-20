using System.Security.Cryptography;
using DocGrouping.Application.Interfaces;
using DocGrouping.Domain.Entities;
using DocGrouping.Domain.Interfaces;
using DocGrouping.Infrastructure.TextProcessing;
using Microsoft.Extensions.Logging;

namespace DocGrouping.Infrastructure.Services;

public class DocumentIngestionService(
	IDocumentRepository documentRepository,
	TextNormalizer normalizer,
	DocumentFingerprinter fingerprinter,
	PdfTextExtractor pdfExtractor,
	ILogger<DocumentIngestionService> logger) : IDocumentIngestionService
{
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

		logger.LogInformation("Ingested document {FileName}: {WordCount} words, text_hash={TextHash}",
			fileName, tokens.Count, textHash[..16]);

		return document;
	}

	public async Task<Document> IngestTextAsync(string fileName, string text, string? documentType = null, CancellationToken ct = default)
	{
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
