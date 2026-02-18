using DocGrouping.Domain.Enums;

namespace DocGrouping.Domain.Entities;

public class ProcessingJob
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public string JobType { get; set; } = string.Empty;
	public JobStatus Status { get; set; } = JobStatus.Pending;
	public int TotalDocuments { get; set; }
	public int ProcessedDocuments { get; set; }
	public string? CurrentPhase { get; set; }
	public string? ErrorMessage { get; set; }
	public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
	public DateTimeOffset? StartedAt { get; set; }
	public DateTimeOffset? CompletedAt { get; set; }
}
