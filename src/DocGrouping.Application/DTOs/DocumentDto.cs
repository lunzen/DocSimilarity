namespace DocGrouping.Application.DTOs;

public class DocumentDto
{
	public Guid Id { get; set; }
	public string FileName { get; set; } = string.Empty;
	public int WordCount { get; set; }
	public string? DocumentType { get; set; }
	public string TextHashPrefix { get; set; } = string.Empty;
	public string FuzzyHashPrefix { get; set; } = string.Empty;
	public string PreviewText { get; set; } = string.Empty;
	public bool IsCanonical { get; set; }
	public DocumentQualityDto? Quality { get; set; }
	public Dictionary<string, object?> Metadata { get; set; } = [];
	public DateTimeOffset CreatedAt { get; set; }
	public bool HasPdf { get; set; }
}

public class DocumentDetailDto : DocumentDto
{
	public string OriginalText { get; set; } = string.Empty;
	public string NormalizedText { get; set; } = string.Empty;
	public string TextHash { get; set; } = string.Empty;
	public string FuzzyHash { get; set; } = string.Empty;
	public string FileHash { get; set; } = string.Empty;
	public long FileSizeBytes { get; set; }
}

public class DocumentQualityDto
{
	public double OcrQuality { get; set; }
	public double Completeness { get; set; }
	public double OverallScore { get; set; }
	public List<string> Artifacts { get; set; } = [];
	public int ArtifactCount { get; set; }
}
