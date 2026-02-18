using System.Text.Json;
using DocGrouping.Application.DTOs;
using DocGrouping.Domain.Entities;
using DocGrouping.Domain.Enums;
using DocGrouping.Domain.Interfaces;
using DocGrouping.Infrastructure.Rules;
using Microsoft.AspNetCore.Mvc;

namespace DocGrouping.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RulesController(
	IBusinessRuleRepository ruleRepository,
	RulesEngine rulesEngine) : ControllerBase
{
	[HttpGet]
	public async Task<IActionResult> GetAll(CancellationToken ct)
	{
		var rules = await ruleRepository.GetAllAsync(ct);
		var dtos = rules.Select(MapToDto).OrderBy(r => r.Priority).ToList();
		return Ok(dtos);
	}

	[HttpGet("{ruleId}")]
	public async Task<IActionResult> GetByRuleId(string ruleId, CancellationToken ct)
	{
		var rule = await ruleRepository.GetByRuleIdAsync(ruleId, ct);
		if (rule is null) return NotFound();
		return Ok(MapToDto(rule));
	}

	[HttpPost]
	public async Task<IActionResult> Create([FromBody] CreateBusinessRuleDto dto, CancellationToken ct)
	{
		var ruleId = $"custom_{Guid.NewGuid():N}"[..24];

		var rule = new BusinessRule
		{
			Id = Guid.NewGuid(),
			RuleId = ruleId,
			Name = dto.Name,
			Description = dto.Description,
			RuleType = Enum.Parse<RuleType>(dto.RuleType),
			Action = Enum.Parse<RuleAction>(dto.Action),
			Priority = dto.Priority,
			Enabled = dto.Enabled,
			Conditions = string.IsNullOrEmpty(dto.ConditionsJson) ? null : JsonDocument.Parse(dto.ConditionsJson),
			CreatedAt = DateTimeOffset.UtcNow,
			UpdatedAt = DateTimeOffset.UtcNow,
		};

		await ruleRepository.AddAsync(rule, ct);

		rulesEngine.AddRule(new RuleDefinition
		{
			RuleId = rule.RuleId,
			Name = rule.Name,
			Description = rule.Description,
			RuleType = rule.RuleType,
			Action = rule.Action,
			Priority = rule.Priority,
			Enabled = rule.Enabled,
		});

		return CreatedAtAction(nameof(GetByRuleId), new { ruleId = rule.RuleId }, MapToDto(rule));
	}

	[HttpPut("{ruleId}")]
	public async Task<IActionResult> Update(string ruleId, [FromBody] UpdateBusinessRuleDto dto, CancellationToken ct)
	{
		var rule = await ruleRepository.GetByRuleIdAsync(ruleId, ct);
		if (rule is null) return NotFound();

		if (dto.Name is not null) rule.Name = dto.Name;
		if (dto.Description is not null) rule.Description = dto.Description;
		if (dto.RuleType is not null) rule.RuleType = Enum.Parse<RuleType>(dto.RuleType);
		if (dto.Action is not null) rule.Action = Enum.Parse<RuleAction>(dto.Action);
		if (dto.Priority.HasValue) rule.Priority = dto.Priority.Value;
		if (dto.Enabled.HasValue) rule.Enabled = dto.Enabled.Value;
		if (dto.ConditionsJson is not null) rule.Conditions = JsonDocument.Parse(dto.ConditionsJson);
		rule.UpdatedAt = DateTimeOffset.UtcNow;

		await ruleRepository.UpdateAsync(rule, ct);

		rulesEngine.UpdateRule(ruleId, r =>
		{
			r.Name = rule.Name;
			r.Description = rule.Description;
			r.RuleType = rule.RuleType;
			r.Action = rule.Action;
			r.Priority = rule.Priority;
			r.Enabled = rule.Enabled;
		});

		return Ok(MapToDto(rule));
	}

	[HttpDelete("{ruleId}")]
	public async Task<IActionResult> Delete(string ruleId, CancellationToken ct)
	{
		var rule = await ruleRepository.GetByRuleIdAsync(ruleId, ct);
		if (rule is null) return NotFound();

		await ruleRepository.DeleteAsync(rule.Id, ct);
		rulesEngine.RemoveRule(ruleId);

		return NoContent();
	}

	[HttpPost("{ruleId}/toggle")]
	public async Task<IActionResult> Toggle(string ruleId, CancellationToken ct)
	{
		var rule = await ruleRepository.GetByRuleIdAsync(ruleId, ct);
		if (rule is null) return NotFound();

		rule.Enabled = !rule.Enabled;
		rule.UpdatedAt = DateTimeOffset.UtcNow;
		await ruleRepository.UpdateAsync(rule, ct);

		rulesEngine.UpdateRule(ruleId, r => r.Enabled = rule.Enabled);

		return Ok(new { rule.RuleId, rule.Enabled });
	}

	private static BusinessRuleDto MapToDto(BusinessRule r) => new()
	{
		Id = r.Id,
		RuleId = r.RuleId,
		Name = r.Name,
		Description = r.Description,
		RuleType = r.RuleType.ToString(),
		Action = r.Action.ToString(),
		Priority = r.Priority,
		Enabled = r.Enabled,
		ConditionsJson = r.Conditions?.RootElement.ToString(),
	};
}
