using DocGrouping.Domain.Enums;

namespace DocGrouping.Domain.Entities;

public class DocumentGroup
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public int GroupNumber { get; set; }
	public MatchConfidence Confidence { get; set; } = MatchConfidence.None;
	public string MatchReason { get; set; } = string.Empty;
	public Guid? CanonicalDocumentId { get; set; }
	public int DocumentCount { get; set; }
	public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
	public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

	// Navigation
	public Document? CanonicalDocument { get; set; }
	public ICollection<DocumentGroupMembership> Memberships { get; set; } = [];
}
