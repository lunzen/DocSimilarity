using DocGrouping.Application.DTOs;
using DocGrouping.Domain.Entities;

namespace DocGrouping.Application.Interfaces;

public interface IGroupingOrchestrator
{
	Task<List<DocumentGroup>> GroupAllDocumentsAsync(CancellationToken ct = default);
	Task<List<DocumentGroup>> GroupAllDocumentsAsync(IProgress<GroupingProgress> progress, CancellationToken ct = default);
	Task<DocumentGroup> GroupSingleDocumentAsync(Document document, CancellationToken ct = default);
	Task<StatisticsDto> GetStatisticsAsync(CancellationToken ct = default);
}

public record GroupingProgress(string Phase, string Message, int PercentComplete, GroupingMetrics? Metrics = null);

public class GroupingMetrics
{
	public int TotalDocuments { get; set; }
	public double Phase1Seconds { get; set; }
	public int Phase1Groups { get; set; }
	public double Phase2Seconds { get; set; }
	public int Phase2Groups { get; set; }
	public double Phase3Seconds { get; set; }
	public int Phase3Groups { get; set; }
	public long Phase3PairsCompared { get; set; }
	public int Phase3UngroupedCount { get; set; }
	public long Phase3CandidatePairs { get; set; }
	public long Phase3VerifiedPairs { get; set; }
	public double Phase4Seconds { get; set; }
	public int Phase4Singletons { get; set; }
	public double TotalSeconds { get; set; }
	public int TotalGroups { get; set; }
}
