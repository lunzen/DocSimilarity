using System.Text.Json;
using DocGrouping.Domain.Enums;

namespace DocGrouping.Domain.Entities;

public class BusinessRule
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public string RuleId { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public RuleType RuleType { get; set; }
	public RuleAction Action { get; set; }
	public int Priority { get; set; }
	public bool Enabled { get; set; } = true;
	public JsonDocument? Conditions { get; set; }
	public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
	public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
