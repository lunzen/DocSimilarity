using DocGrouping.Domain.Entities;
using DocGrouping.Domain.Enums;
using DocGrouping.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DocGrouping.Infrastructure.Persistence.Repositories;

public class DocumentGroupRepository(DocGroupingDbContext db) : IDocumentGroupRepository
{
	public async Task<DocumentGroup?> GetByIdAsync(Guid id, CancellationToken ct = default)
		=> await db.DocumentGroups
			.Include(g => g.Memberships).ThenInclude(m => m.Document)
			.Include(g => g.CanonicalDocument)
			.FirstOrDefaultAsync(g => g.Id == id, ct);

	public async Task<DocumentGroup?> GetByGroupNumberAsync(int groupNumber, CancellationToken ct = default)
		=> await db.DocumentGroups
			.Include(g => g.Memberships).ThenInclude(m => m.Document)
			.Include(g => g.CanonicalDocument)
			.FirstOrDefaultAsync(g => g.GroupNumber == groupNumber, ct);

	public async Task<List<DocumentGroup>> GetAllAsync(CancellationToken ct = default)
		=> await db.DocumentGroups
			.Include(g => g.Memberships).ThenInclude(m => m.Document)
			.Include(g => g.CanonicalDocument)
			.OrderBy(g => g.GroupNumber)
			.ToListAsync(ct);

	public async Task<List<DocumentGroup>> GetByConfidenceAsync(MatchConfidence confidence, CancellationToken ct = default)
		=> await db.DocumentGroups
			.Include(g => g.Memberships).ThenInclude(m => m.Document)
			.Where(g => g.Confidence == confidence)
			.OrderBy(g => g.GroupNumber)
			.ToListAsync(ct);

	public async Task<List<DocumentGroup>> GetPagedAsync(int page, int pageSize, MatchConfidence? confidence = null, CancellationToken ct = default)
	{
		var query = db.DocumentGroups
			.Include(g => g.Memberships).ThenInclude(m => m.Document)
			.Include(g => g.CanonicalDocument)
			.AsQueryable();

		if (confidence.HasValue)
			query = query.Where(g => g.Confidence == confidence.Value);

		return await query
			.OrderBy(g => g.GroupNumber)
			.Skip((page - 1) * pageSize)
			.Take(pageSize)
			.ToListAsync(ct);
	}

	public async Task<int> GetCountAsync(MatchConfidence? confidence = null, CancellationToken ct = default)
	{
		var query = db.DocumentGroups.AsQueryable();
		if (confidence.HasValue)
			query = query.Where(g => g.Confidence == confidence.Value);
		return await query.CountAsync(ct);
	}

	public async Task<int> GetNextGroupNumberAsync(CancellationToken ct = default)
	{
		var max = await db.DocumentGroups.MaxAsync(g => (int?)g.GroupNumber, ct);
		return (max ?? 0) + 1;
	}

	public async Task AddAsync(DocumentGroup group, CancellationToken ct = default)
	{
		db.DocumentGroups.Add(group);
		await db.SaveChangesAsync(ct);
	}

	public async Task AddRangeAsync(IEnumerable<DocumentGroup> groups, CancellationToken ct = default)
	{
		db.DocumentGroups.AddRange(groups);
		await db.SaveChangesAsync(ct);
	}

	public async Task UpdateAsync(DocumentGroup group, CancellationToken ct = default)
	{
		group.UpdatedAt = DateTimeOffset.UtcNow;

		// Fix phantom-Modified memberships caused by EF Core navigation fix-up.
		// When the same group is loaded multiple times and memberships are added,
		// existing memberships can be marked Modified even though no values changed.
		foreach (var entry in db.ChangeTracker.Entries<DocumentGroupMembership>()
			.Where(e => e.State == EntityState.Modified))
		{
			var hasRealChange = entry.Properties.Any(p => p.IsModified &&
				!Equals(p.OriginalValue, p.CurrentValue));
			if (!hasRealChange)
				entry.State = EntityState.Unchanged;
		}

		await db.SaveChangesAsync(ct);
	}

	public async Task DeleteAsync(Guid id, CancellationToken ct = default)
	{
		var group = await db.DocumentGroups.FindAsync([id], ct);
		if (group != null)
		{
			db.DocumentGroups.Remove(group);
			await db.SaveChangesAsync(ct);
		}
	}

	public async Task DeleteAllAsync(CancellationToken ct = default)
	{
		db.DocumentGroupMemberships.RemoveRange(db.DocumentGroupMemberships);
		db.DocumentGroups.RemoveRange(db.DocumentGroups);
		await db.SaveChangesAsync(ct);
	}
}
