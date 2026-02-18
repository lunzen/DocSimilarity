namespace DocGrouping.Application.DTOs;

public class StatisticsDto
{
	public int TotalDocuments { get; set; }
	public int TotalGroups { get; set; }
	public int GroupsWithDuplicates { get; set; }
	public int SingletonGroups { get; set; }
	public Dictionary<string, int> ConfidenceBreakdown { get; set; } = [];
	public double DeduplicationRatio { get; set; }
}
