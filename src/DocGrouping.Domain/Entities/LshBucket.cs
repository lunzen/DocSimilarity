namespace DocGrouping.Domain.Entities;

public class LshBucket
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public int BandIndex { get; set; }
	public string BucketHash { get; set; } = string.Empty;
	public Guid DocumentId { get; set; }

	// Navigation
	public Document Document { get; set; } = null!;
}
