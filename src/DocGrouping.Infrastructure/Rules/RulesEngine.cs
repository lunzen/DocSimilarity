using System.Text.Json;
using System.Text.RegularExpressions;
using DocGrouping.Domain.Enums;

namespace DocGrouping.Infrastructure.Rules;

public class RuleDefinition
{
	public string RuleId { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public RuleType RuleType { get; set; }
	public int Priority { get; set; }
	public bool Enabled { get; set; } = true;
	public Dictionary<string, JsonElement> Conditions { get; set; } = [];
	public RuleAction Action { get; set; } = RuleAction.PreventGroup;
}

public class RuleEvaluation
{
	public List<AppliedRule> AppliedRules { get; set; } = [];
	public Dictionary<string, object?> RuleFlags { get; set; } = [];
}

public class AppliedRule
{
	public string RuleId { get; set; } = string.Empty;
	public string RuleName { get; set; } = string.Empty;
	public string Action { get; set; } = string.Empty;
	public int Priority { get; set; }
}

public class GroupingDecision
{
	public bool ShouldGroup { get; set; }
	public string Confidence { get; set; } = string.Empty;
	public bool RuleModified { get; set; }
	public List<string> AppliedRules { get; set; } = [];
	public List<string> Explanation { get; set; } = [];
}

public class RulesEngine
{
	private readonly List<RuleDefinition> _rules = [];

	public RulesEngine()
	{
		LoadDefaultRules();
	}

	private void LoadDefaultRules()
	{
		AddRule(new RuleDefinition
		{
			RuleId = "default_version_priority",
			Name = "Version Priority: FINAL > DRAFT",
			Description = "When grouping versions, prefer FINAL over DRAFT as canonical",
			RuleType = RuleType.VersionPriority,
			Priority = 10,
			Conditions = JsonToDictionary("""{"version_hierarchy": ["FINAL", "REVISED", "DRAFT", "COPY"]}"""),
			Action = RuleAction.SetCanonical
		});

		AddRule(new RuleDefinition
		{
			RuleId = "default_redaction_separator",
			Name = "Redacted Documents Stay Separate",
			Description = "Documents with [REDACTED] sections are kept separate",
			RuleType = RuleType.MetadataSeparator,
			Priority = 20,
			Conditions = JsonToDictionary("""{"pattern": "\\[REDACTED\\]", "reason": "Redacted content changes document meaning"}"""),
			Action = RuleAction.Separate
		});

		AddRule(new RuleDefinition
		{
			RuleId = "default_bates_significance",
			Name = "Bates Stamps Indicate Legal Significance",
			Description = "Documents with Bates numbers may be legally significant copies",
			RuleType = RuleType.StampSignificance,
			Priority = 30,
			Enabled = false,
			Conditions = JsonToDictionary("""{"stamps": ["BATES:", "[A-Z]{2,6}[-_]?\\d{6,10}"], "keep_grouped": true}"""),
			Action = RuleAction.ForceGroup
		});

		AddRule(new RuleDefinition
		{
			RuleId = "default_document_type_separator",
			Name = "Separate Different Document Types",
			Description = "Don't group documents of different types",
			RuleType = RuleType.DocumentTypeMatch,
			Priority = 15,
			Conditions = JsonToDictionary("""{"require_same_type": true}"""),
			Action = RuleAction.PreventGroup
		});

		AddRule(new RuleDefinition
		{
			RuleId = "default_source_folder_separator",
			Name = "Separate Documents from Different Folders",
			Description = "Documents from different source folders likely belong to different transactions",
			RuleType = RuleType.SameSourceFolder,
			Priority = 25,
			Enabled = false,
			Conditions = JsonToDictionary("""{"require_same_folder": true}"""),
			Action = RuleAction.PreventGroup
		});

		AddRule(new RuleDefinition
		{
			RuleId = "default_date_proximity",
			Name = "Date Proximity Check",
			Description = "Separate documents with dates more than 30 days apart",
			RuleType = RuleType.DateProximity,
			Priority = 35,
			Enabled = false,
			Conditions = JsonToDictionary("""{"max_days_difference": 30}"""),
			Action = RuleAction.PreventGroup
		});
	}

	private static Dictionary<string, JsonElement> JsonToDictionary(string json)
	{
		var doc = JsonDocument.Parse(json);
		var dict = new Dictionary<string, JsonElement>();
		foreach (var prop in doc.RootElement.EnumerateObject())
		{
			dict[prop.Name] = prop.Value.Clone();
		}
		return dict;
	}

	public void AddRule(RuleDefinition rule)
	{
		_rules.Add(rule);
		_rules.Sort((a, b) => a.Priority.CompareTo(b.Priority));
	}

	public bool RemoveRule(string ruleId)
	{
		return _rules.RemoveAll(r => r.RuleId == ruleId) > 0;
	}

	public bool UpdateRule(string ruleId, Action<RuleDefinition> update)
	{
		var rule = _rules.FirstOrDefault(r => r.RuleId == ruleId);
		if (rule == null) return false;
		update(rule);
		_rules.Sort((a, b) => a.Priority.CompareTo(b.Priority));
		return true;
	}

	public RuleDefinition? GetRule(string ruleId) => _rules.FirstOrDefault(r => r.RuleId == ruleId);

	public List<RuleDefinition> GetAllRules() => _rules.OrderBy(r => r.Priority).ToList();

	public RuleEvaluation EvaluateDocument(string originalText, Dictionary<string, object?>? metadata)
	{
		var eval = new RuleEvaluation();

		foreach (var rule in _rules.Where(r => r.Enabled))
		{
			var matched = false;

			switch (rule.RuleType)
			{
				case RuleType.VersionPriority:
					if (rule.Conditions.TryGetValue("version_hierarchy", out var hierarchy))
					{
						var versions = hierarchy.EnumerateArray().Select(v => v.GetString()!).ToList();
						foreach (var version in versions)
						{
							if (originalText.Contains(version, StringComparison.OrdinalIgnoreCase))
							{
								matched = true;
								eval.RuleFlags["version"] = version;
								eval.RuleFlags["version_rank"] = versions.IndexOf(version);
								break;
							}
						}
					}
					break;

				case RuleType.MetadataSeparator:
					if (rule.Conditions.TryGetValue("pattern", out var patternEl))
					{
						var pattern = patternEl.GetString()!;
						if (Regex.IsMatch(originalText, pattern, RegexOptions.IgnoreCase))
						{
							matched = true;
							eval.RuleFlags["has_redactions"] = true;
						}
					}
					break;

				case RuleType.StampSignificance:
					if (rule.Conditions.TryGetValue("stamps", out var stampsEl))
					{
						foreach (var stamp in stampsEl.EnumerateArray())
						{
							if (Regex.IsMatch(originalText, stamp.GetString()!, RegexOptions.IgnoreCase))
							{
								matched = true;
								eval.RuleFlags["has_significant_stamps"] = true;
								break;
							}
						}
					}
					break;

				case RuleType.TextPattern:
					if (rule.Conditions.TryGetValue("pattern", out var textPatternEl))
					{
						if (Regex.IsMatch(originalText, textPatternEl.GetString()!, RegexOptions.IgnoreCase))
							matched = true;
					}
					break;

				case RuleType.DocumentTypeMatch:
					if (metadata?.TryGetValue("document_type", out var docType) == true && docType != null)
					{
						eval.RuleFlags["document_type"] = docType;
						matched = true;
					}
					break;

				case RuleType.SameParties:
					if (metadata?.TryGetValue("parties", out var parties) == true && parties != null)
					{
						eval.RuleFlags["parties"] = parties;
						matched = true;
					}
					break;

				case RuleType.SameSourceFolder:
					if (metadata?.TryGetValue("source_folder", out var folder) == true && folder != null)
					{
						eval.RuleFlags["source_folder"] = folder;
						matched = true;
					}
					break;

				case RuleType.TagMatch:
					if (metadata?.TryGetValue("tags", out var tags) == true && tags != null)
					{
						eval.RuleFlags["tags"] = tags;
						matched = true;
					}
					break;

				case RuleType.BatesSequence:
					if (metadata?.TryGetValue("bates_range", out var bates) == true && bates != null)
					{
						eval.RuleFlags["bates_range"] = bates;
						matched = true;
					}
					break;

				case RuleType.DateProximity:
					var docDate = metadata?.GetValueOrDefault("document_date")
						?? metadata?.GetValueOrDefault("execution_date");
					if (docDate != null)
					{
						eval.RuleFlags["primary_date"] = docDate;
						matched = true;
					}
					break;
			}

			if (matched)
			{
				eval.AppliedRules.Add(new AppliedRule
				{
					RuleId = rule.RuleId,
					RuleName = rule.Name,
					Action = rule.Action.ToString(),
					Priority = rule.Priority
				});
			}
		}

		return eval;
	}

	public GroupingDecision ShouldGroup(
		string originalText1, Dictionary<string, object?>? metadata1,
		string originalText2, Dictionary<string, object?>? metadata2,
		string baseConfidence)
	{
		var eval1 = EvaluateDocument(originalText1, metadata1);
		var eval2 = EvaluateDocument(originalText2, metadata2);

		var decision = new GroupingDecision
		{
			ShouldGroup = baseConfidence is "very_high" or "high",
			Confidence = baseConfidence
		};

		// Check PREVENT_GROUP rules
		foreach (var rule in _rules.Where(r => r.Enabled && r.Action is RuleAction.PreventGroup or RuleAction.Separate))
		{
			switch (rule.RuleType)
			{
				case RuleType.MetadataSeparator:
					var hasRedaction1 = eval1.RuleFlags.GetValueOrDefault("has_redactions") as bool? ?? false;
					var hasRedaction2 = eval2.RuleFlags.GetValueOrDefault("has_redactions") as bool? ?? false;
					if (hasRedaction1 != hasRedaction2)
					{
						decision.ShouldGroup = false;
						decision.RuleModified = true;
						decision.AppliedRules.Add(rule.RuleId);
						decision.Explanation.Add($"Rule '{rule.Name}': One document has redactions, other doesn't");
					}
					break;

				case RuleType.DocumentTypeMatch:
					var type1 = eval1.RuleFlags.GetValueOrDefault("document_type")?.ToString();
					var type2 = eval2.RuleFlags.GetValueOrDefault("document_type")?.ToString();
					if (type1 != null && type2 != null && type1 != type2)
					{
						var requireSame = GetConditionBool(rule.Conditions, "require_same_type", true);
						if (requireSame)
						{
							decision.ShouldGroup = false;
							decision.RuleModified = true;
							decision.AppliedRules.Add(rule.RuleId);
							decision.Explanation.Add($"Rule '{rule.Name}': Document types differ ({type1} vs {type2})");
						}
					}
					break;

				case RuleType.SameSourceFolder:
					var folder1 = eval1.RuleFlags.GetValueOrDefault("source_folder")?.ToString();
					var folder2 = eval2.RuleFlags.GetValueOrDefault("source_folder")?.ToString();
					if (folder1 != null && folder2 != null && folder1 != folder2)
					{
						var requireSameFolder = GetConditionBool(rule.Conditions, "require_same_folder", true);
						if (requireSameFolder)
						{
							decision.ShouldGroup = false;
							decision.RuleModified = true;
							decision.AppliedRules.Add(rule.RuleId);
							decision.Explanation.Add($"Rule '{rule.Name}': Source folders differ ({folder1} vs {folder2})");
						}
					}
					break;

				case RuleType.DateProximity:
					var date1Str = eval1.RuleFlags.GetValueOrDefault("primary_date")?.ToString();
					var date2Str = eval2.RuleFlags.GetValueOrDefault("primary_date")?.ToString();
					if (date1Str != null && date2Str != null
						&& DateTime.TryParse(date1Str, out var d1)
						&& DateTime.TryParse(date2Str, out var d2))
					{
						var daysDiff = Math.Abs((d1 - d2).Days);
						var maxDays = GetConditionInt(rule.Conditions, "max_days_difference", 7);
						if (daysDiff > maxDays)
						{
							decision.ShouldGroup = false;
							decision.RuleModified = true;
							decision.AppliedRules.Add(rule.RuleId);
							decision.Explanation.Add($"Rule '{rule.Name}': Dates differ by {daysDiff} days (max: {maxDays})");
						}
					}
					break;
			}
		}

		// Check FORCE_GROUP rules
		if (decision.ShouldGroup)
		{
			foreach (var rule in _rules.Where(r => r.Enabled && r.Action == RuleAction.ForceGroup))
			{
				switch (rule.RuleType)
				{
					case RuleType.StampSignificance:
						var hasStamps1 = eval1.RuleFlags.GetValueOrDefault("has_significant_stamps") as bool? ?? false;
						var hasStamps2 = eval2.RuleFlags.GetValueOrDefault("has_significant_stamps") as bool? ?? false;
						if (hasStamps1 || hasStamps2)
						{
							decision.AppliedRules.Add(rule.RuleId);
							decision.Explanation.Add($"Rule '{rule.Name}': Documents grouped despite stamps (rule enabled)");
						}
						break;

					case RuleType.SameParties:
						var parties1 = eval1.RuleFlags.GetValueOrDefault("parties");
						var parties2 = eval2.RuleFlags.GetValueOrDefault("parties");
						if (parties1 != null && parties2 != null && parties1.ToString() == parties2.ToString())
						{
							decision.ShouldGroup = true;
							decision.RuleModified = true;
							decision.AppliedRules.Add(rule.RuleId);
							decision.Explanation.Add($"Rule '{rule.Name}': Same parties");
						}
						break;
				}
			}
		}

		return decision;
	}

	public int SelectCanonicalIndex(List<(string OriginalText, Dictionary<string, object?>? Metadata)> documents)
	{
		var bestIndex = 0;
		var bestScore = int.MinValue;

		for (var i = 0; i < documents.Count; i++)
		{
			var (text, metadata) = documents[i];
			var eval = EvaluateDocument(text, metadata);
			var score = 0;

			var versionRank = eval.RuleFlags.GetValueOrDefault("version_rank") as int? ?? 999;
			score = 1000 - versionRank;

			if (eval.RuleFlags.GetValueOrDefault("has_redactions") is true)
				score -= 500;
			if (eval.RuleFlags.GetValueOrDefault("has_significant_stamps") is true)
				score -= 100;

			if (score > bestScore)
			{
				bestScore = score;
				bestIndex = i;
			}
		}

		return bestIndex;
	}

	private static bool GetConditionBool(Dictionary<string, JsonElement> conditions, string key, bool defaultValue)
	{
		if (conditions.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.True)
			return true;
		if (conditions.TryGetValue(key, out el) && el.ValueKind == JsonValueKind.False)
			return false;
		return defaultValue;
	}

	private static int GetConditionInt(Dictionary<string, JsonElement> conditions, string key, int defaultValue)
	{
		if (conditions.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.Number)
			return el.GetInt32();
		return defaultValue;
	}
}
