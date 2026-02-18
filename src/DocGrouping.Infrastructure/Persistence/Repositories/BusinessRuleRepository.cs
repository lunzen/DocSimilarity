using DocGrouping.Domain.Entities;
using DocGrouping.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DocGrouping.Infrastructure.Persistence.Repositories;

public class BusinessRuleRepository(DocGroupingDbContext db) : IBusinessRuleRepository
{
	public async Task<BusinessRule?> GetByIdAsync(Guid id, CancellationToken ct = default)
		=> await db.BusinessRules.FindAsync([id], ct);

	public async Task<BusinessRule?> GetByRuleIdAsync(string ruleId, CancellationToken ct = default)
		=> await db.BusinessRules.FirstOrDefaultAsync(r => r.RuleId == ruleId, ct);

	public async Task<List<BusinessRule>> GetAllAsync(CancellationToken ct = default)
		=> await db.BusinessRules.OrderBy(r => r.Priority).ToListAsync(ct);

	public async Task<List<BusinessRule>> GetEnabledAsync(CancellationToken ct = default)
		=> await db.BusinessRules.Where(r => r.Enabled).OrderBy(r => r.Priority).ToListAsync(ct);

	public async Task AddAsync(BusinessRule rule, CancellationToken ct = default)
	{
		db.BusinessRules.Add(rule);
		await db.SaveChangesAsync(ct);
	}

	public async Task UpdateAsync(BusinessRule rule, CancellationToken ct = default)
	{
		rule.UpdatedAt = DateTimeOffset.UtcNow;
		db.BusinessRules.Update(rule);
		await db.SaveChangesAsync(ct);
	}

	public async Task DeleteAsync(Guid id, CancellationToken ct = default)
	{
		var rule = await db.BusinessRules.FindAsync([id], ct);
		if (rule != null)
		{
			db.BusinessRules.Remove(rule);
			await db.SaveChangesAsync(ct);
		}
	}
}
