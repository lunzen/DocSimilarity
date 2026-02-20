using System.Text.Json;

namespace DocGrouping.Domain.Entities;

public class Document
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public string FileName { get; set; } = string.Empty;
	public string FilePath { get; set; } = string.Empty;
	public long FileSizeBytes { get; set; }
	public string FileHash { get; set; } = string.Empty;
	public string OriginalText { get; set; } = string.Empty;
	public string NormalizedText { get; set; } = string.Empty;
	public string TextHash { get; set; } = string.Empty;
	public string FuzzyHash { get; set; } = string.Empty;
	public int WordCount { get; set; }
	public string? DocumentType { get; set; }
	public DateOnly? DocumentDate { get; set; }
	public JsonDocument? Parties { get; set; }
	public string? BatesRange { get; set; }
	public string? SourceFolder { get; set; }
	public JsonDocument? Tags { get; set; }
	public JsonDocument? CustomMetadata { get; set; }
	public bool IsCanonicalReference { get; set; }
	public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

	// Navigation
	public DocumentGroupMembership? GroupMembership { get; set; }
	public MinHashSignature? MinHashSignature { get; set; }
	public ICollection<LshBucket> LshBuckets { get; set; } = [];
}
