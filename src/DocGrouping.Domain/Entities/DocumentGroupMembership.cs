namespace DocGrouping.Domain.Entities;

public class DocumentGroupMembership
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public Guid DocumentId { get; set; }
	public Guid GroupId { get; set; }
	public bool IsCanonical { get; set; }
	public decimal SimilarityScore { get; set; }

	// Navigation
	public Document Document { get; set; } = null!;
	public DocumentGroup Group { get; set; } = null!;
}
