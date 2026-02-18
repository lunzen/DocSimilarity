using DocGrouping.Domain.Entities;
using DocGrouping.Domain.Enums;

namespace DocGrouping.Domain.Interfaces;

public interface IDocumentGroupRepository
{
	Task<DocumentGroup?> GetByIdAsync(Guid id, CancellationToken ct = default);
	Task<DocumentGroup?> GetByGroupNumberAsync(int groupNumber, CancellationToken ct = default);
	Task<List<DocumentGroup>> GetAllAsync(CancellationToken ct = default);
	Task<List<DocumentGroup>> GetByConfidenceAsync(MatchConfidence confidence, CancellationToken ct = default);
	Task<List<DocumentGroup>> GetPagedAsync(int page, int pageSize, MatchConfidence? confidence = null, CancellationToken ct = default);
	Task<int> GetCountAsync(MatchConfidence? confidence = null, CancellationToken ct = default);
	Task<int> GetNextGroupNumberAsync(CancellationToken ct = default);
	Task AddAsync(DocumentGroup group, CancellationToken ct = default);
	Task UpdateAsync(DocumentGroup group, CancellationToken ct = default);
	Task DeleteAsync(Guid id, CancellationToken ct = default);
	Task DeleteAllAsync(CancellationToken ct = default);
}
