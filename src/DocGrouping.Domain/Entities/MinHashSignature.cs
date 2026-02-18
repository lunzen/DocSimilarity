namespace DocGrouping.Domain.Entities;

public class MinHashSignature
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public Guid DocumentId { get; set; }
	public int[] Signature { get; set; } = [];

	// Navigation
	public Document Document { get; set; } = null!;
}
