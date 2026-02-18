namespace DocGrouping.Application.DTOs;

public class BusinessRuleDto
{
	public Guid Id { get; set; }
	public string RuleId { get; set; } = "";
	public string Name { get; set; } = "";
	public string Description { get; set; } = "";
	public string RuleType { get; set; } = "";
	public string Action { get; set; } = "";
	public int Priority { get; set; }
	public bool Enabled { get; set; }
	public string? ConditionsJson { get; set; }
}

public class CreateBusinessRuleDto
{
	public string Name { get; set; } = "";
	public string Description { get; set; } = "";
	public string RuleType { get; set; } = "";
	public string Action { get; set; } = "";
	public int Priority { get; set; } = 50;
	public bool Enabled { get; set; } = true;
	public string? ConditionsJson { get; set; }
}

public class UpdateBusinessRuleDto
{
	public string? Name { get; set; }
	public string? Description { get; set; }
	public string? RuleType { get; set; }
	public string? Action { get; set; }
	public int? Priority { get; set; }
	public bool? Enabled { get; set; }
	public string? ConditionsJson { get; set; }
}
