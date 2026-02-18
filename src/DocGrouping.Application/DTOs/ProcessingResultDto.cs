namespace DocGrouping.Application.DTOs;

public class ProcessingResultDto
{
	public Guid JobId { get; set; }
	public List<GroupDto> Groups { get; set; } = [];
	public StatisticsDto Statistics { get; set; } = new();
	public double ProcessingTimeSeconds { get; set; }
}
