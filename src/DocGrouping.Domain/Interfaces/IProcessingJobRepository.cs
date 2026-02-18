using DocGrouping.Domain.Entities;

namespace DocGrouping.Domain.Interfaces;

public interface IProcessingJobRepository
{
	Task<ProcessingJob?> GetByIdAsync(Guid id, CancellationToken ct = default);
	Task<List<ProcessingJob>> GetRecentAsync(int count = 20, CancellationToken ct = default);
	Task AddAsync(ProcessingJob job, CancellationToken ct = default);
	Task UpdateAsync(ProcessingJob job, CancellationToken ct = default);
}
