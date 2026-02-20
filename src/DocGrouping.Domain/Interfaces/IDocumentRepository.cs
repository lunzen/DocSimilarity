using DocGrouping.Domain.Entities;

namespace DocGrouping.Domain.Interfaces;

public interface IDocumentRepository
{
	Task<Document?> GetByIdAsync(Guid id, CancellationToken ct = default);
	Task<Document?> GetByTextHashAsync(string textHash, CancellationToken ct = default);
	Task<Document?> GetByFuzzyHashAsync(string fuzzyHash, CancellationToken ct = default);
	Task<Document?> GetByFileHashAsync(string fileHash, CancellationToken ct = default);
	Task<List<Document>> GetByTextHashesAsync(IEnumerable<string> textHashes, CancellationToken ct = default);
	Task<List<Document>> GetByFuzzyHashesAsync(IEnumerable<string> fuzzyHashes, CancellationToken ct = default);
	Task<List<Document>> GetAllAsync(CancellationToken ct = default);
	Task<List<Document>> GetAllCanonicalsAsync(CancellationToken ct = default);
	Task<List<Document>> GetCanonicalsByDocumentTypeAsync(string documentType, CancellationToken ct = default);
	Task<List<Document>> GetUngroupedAsync(CancellationToken ct = default);
	Task<int> GetCountAsync(CancellationToken ct = default);
	Task AddAsync(Document document, CancellationToken ct = default);
	Task AddRangeAsync(IEnumerable<Document> documents, CancellationToken ct = default);
	Task UpdateAsync(Document document, CancellationToken ct = default);
	Task UpdateRangeAsync(IEnumerable<Document> documents, CancellationToken ct = default);
	Task DeleteAsync(Guid id, CancellationToken ct = default);
}
