using DocGrouping.Domain.Entities;

namespace DocGrouping.Application.Interfaces;

public interface IDocumentIngestionService
{
	Task<Document> IngestFileAsync(string filePath, CancellationToken ct = default);
	Task<Document> IngestTextAsync(string fileName, string text, string? documentType = null, CancellationToken ct = default);
	Task<List<Document>> IngestDirectoryAsync(string directoryPath, CancellationToken ct = default);
}
