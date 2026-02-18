using DocGrouping.Domain.Entities;
using DocGrouping.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DocGrouping.Infrastructure.Persistence.Repositories;

public class ProcessingJobRepository(DocGroupingDbContext db) : IProcessingJobRepository
{
	public async Task<ProcessingJob?> GetByIdAsync(Guid id, CancellationToken ct = default)
		=> await db.ProcessingJobs.FindAsync([id], ct);

	public async Task<List<ProcessingJob>> GetRecentAsync(int count = 20, CancellationToken ct = default)
		=> await db.ProcessingJobs
			.OrderByDescending(j => j.CreatedAt)
			.Take(count)
			.ToListAsync(ct);

	public async Task AddAsync(ProcessingJob job, CancellationToken ct = default)
	{
		db.ProcessingJobs.Add(job);
		await db.SaveChangesAsync(ct);
	}

	public async Task UpdateAsync(ProcessingJob job, CancellationToken ct = default)
	{
		db.ProcessingJobs.Update(job);
		await db.SaveChangesAsync(ct);
	}
}
