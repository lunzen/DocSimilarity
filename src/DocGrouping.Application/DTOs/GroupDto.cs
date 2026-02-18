namespace DocGrouping.Application.DTOs;

public class GroupDto
{
	public Guid Id { get; set; }
	public int GroupNumber { get; set; }
	public string Confidence { get; set; } = string.Empty;
	public string MatchReason { get; set; } = string.Empty;
	public int DocumentCount { get; set; }
	public string? CanonicalFileName { get; set; }
	public List<DocumentDto> Documents { get; set; } = [];
	public MatchExplanationDto? MatchExplanation { get; set; }
}

public class MatchExplanationDto
{
	public double JaccardSimilarity { get; set; }
	public double OverlapCoefficient { get; set; }
	public double FuzzySignatureMatch { get; set; }
	public int CommonTokensCount { get; set; }
	public List<string> CommonKeywords { get; set; } = [];
	public int TotalTokensDoc1 { get; set; }
	public int TotalTokensDoc2 { get; set; }
	public string MatchType { get; set; } = string.Empty;
}
