using DocGrouping.Domain.Entities;

namespace DocGrouping.Domain.Interfaces;

public interface IBusinessRuleRepository
{
	Task<BusinessRule?> GetByIdAsync(Guid id, CancellationToken ct = default);
	Task<BusinessRule?> GetByRuleIdAsync(string ruleId, CancellationToken ct = default);
	Task<List<BusinessRule>> GetAllAsync(CancellationToken ct = default);
	Task<List<BusinessRule>> GetEnabledAsync(CancellationToken ct = default);
	Task AddAsync(BusinessRule rule, CancellationToken ct = default);
	Task UpdateAsync(BusinessRule rule, CancellationToken ct = default);
	Task DeleteAsync(Guid id, CancellationToken ct = default);
}
