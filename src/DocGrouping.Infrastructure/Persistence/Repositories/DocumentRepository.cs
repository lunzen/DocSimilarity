using DocGrouping.Domain.Entities;
using DocGrouping.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DocGrouping.Infrastructure.Persistence.Repositories;

public class DocumentRepository(DocGroupingDbContext db) : IDocumentRepository
{
	public async Task<Document?> GetByIdAsync(Guid id, CancellationToken ct = default)
		=> await db.Documents.FindAsync([id], ct);

	public async Task<Document?> GetByTextHashAsync(string textHash, CancellationToken ct = default)
		=> await db.Documents.FirstOrDefaultAsync(d => d.TextHash == textHash, ct);

	public async Task<Document?> GetByFuzzyHashAsync(string fuzzyHash, CancellationToken ct = default)
		=> await db.Documents.FirstOrDefaultAsync(d => d.FuzzyHash == fuzzyHash, ct);

	public async Task<Document?> GetByFileHashAsync(string fileHash, CancellationToken ct = default)
		=> await db.Documents.FirstOrDefaultAsync(d => d.FileHash == fileHash, ct);

	public async Task<List<Document>> GetByTextHashesAsync(IEnumerable<string> textHashes, CancellationToken ct = default)
		=> await db.Documents.Where(d => textHashes.Contains(d.TextHash)).ToListAsync(ct);

	public async Task<List<Document>> GetByFuzzyHashesAsync(IEnumerable<string> fuzzyHashes, CancellationToken ct = default)
		=> await db.Documents.Where(d => fuzzyHashes.Contains(d.FuzzyHash)).ToListAsync(ct);

	public async Task<List<Document>> GetAllAsync(CancellationToken ct = default)
		=> await db.Documents.ToListAsync(ct);

	public async Task<List<Document>> GetAllCanonicalsAsync(CancellationToken ct = default)
		=> await db.Documents.Where(d => d.IsCanonicalReference).ToListAsync(ct);

	public async Task<List<Document>> GetCanonicalsByDocumentTypeAsync(string documentType, CancellationToken ct = default)
		=> await db.Documents.Where(d => d.IsCanonicalReference && d.DocumentType == documentType).ToListAsync(ct);

	public async Task<List<Document>> GetUngroupedAsync(CancellationToken ct = default)
		=> await db.Documents.Where(d => d.GroupMembership == null).ToListAsync(ct);

	public async Task<int> GetCountAsync(CancellationToken ct = default)
		=> await db.Documents.CountAsync(ct);

	public async Task AddAsync(Document document, CancellationToken ct = default)
	{
		db.Documents.Add(document);
		await db.SaveChangesAsync(ct);
	}

	public async Task AddRangeAsync(IEnumerable<Document> documents, CancellationToken ct = default)
	{
		db.Documents.AddRange(documents);
		await db.SaveChangesAsync(ct);
	}

	public async Task UpdateAsync(Document document, CancellationToken ct = default)
	{
		db.Documents.Update(document);
		await db.SaveChangesAsync(ct);
	}

	public async Task UpdateRangeAsync(IEnumerable<Document> documents, CancellationToken ct = default)
	{
		db.Documents.UpdateRange(documents);
		await db.SaveChangesAsync(ct);
	}

	public async Task DeleteAsync(Guid id, CancellationToken ct = default)
	{
		var doc = await db.Documents.FindAsync([id], ct);
		if (doc != null)
		{
			db.Documents.Remove(doc);
			await db.SaveChangesAsync(ct);
		}
	}
}
