using System.Text.RegularExpressions;

namespace DocGrouping.Infrastructure.TextProcessing;

public class RedactionSimulator
{
	private static readonly Dictionary<string, string> RedactionPatterns = new()
	{
		["dollar_amounts"] = @"\$[\d,]+\.?\d*",
		["ssn"] = @"\b\d{3}-\d{2}-\d{4}\b",
		["phone"] = @"\b\d{3}-\d{3}-\d{4}\b",
		["email"] = @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b",
		["dates"] = @"\b(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]* \d{1,2},? \d{4}\b",
		["numeric_dates"] = @"\b\d{1,2}/\d{1,2}/\d{2,4}\b",
		["percentages"] = @"\b\d+\.?\d*%",
		["account_numbers"] = @"\b[A-Z]{2,4}-\d{6,10}\b",
	};

	private static readonly Dictionary<string, (int Min, int Max)> RedactionCounts = new()
	{
		["none"] = (0, 0),
		["light"] = (5, 10),
		["moderate"] = (10, 20),
		["heavy"] = (20, 40),
	};

	private static readonly Dictionary<string, string> RedactionLabels = new()
	{
		["dollar_amounts"] = "[REDACTED: AMOUNT]",
		["ssn"] = "[REDACTED: SSN]",
		["phone"] = "[REDACTED: PHONE]",
		["email"] = "[REDACTED: EMAIL]",
		["dates"] = "[REDACTED: DATE]",
		["numeric_dates"] = "[REDACTED: DATE]",
		["percentages"] = "[REDACTED: %]",
		["account_numbers"] = "[REDACTED: ACCOUNT]",
		["name"] = "[REDACTED: NAME]",
		["address"] = "[REDACTED: ADDRESS]",
	};

	private static readonly HashSet<string> SkipWords =
	[
		"January", "February", "March", "April", "May", "June",
		"July", "August", "September", "October", "November", "December",
		"Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday",
		"Agreement", "Section", "Article", "Exhibit", "Whereas", "Therefore"
	];

	private readonly Random _rng = new();

	public string ApplyRedactions(string text, string redactionLevel = "light")
	{
		if (redactionLevel == "none" || !RedactionCounts.TryGetValue(redactionLevel, out var counts))
			return text;

		var targetCount = _rng.Next(counts.Min, counts.Max + 1);
		var targets = FindRedactionTargets(text);

		if (targets.Count == 0) return text;

		var numToRedact = Math.Min(targetCount, targets.Count);
		var selected = targets.OrderBy(_ => _rng.Next()).Take(numToRedact).ToList();
		selected.Sort((a, b) => b.Start.CompareTo(a.Start)); // reverse order

		var result = text;
		foreach (var (start, end, matchType) in selected)
		{
			var label = RedactionLabels.GetValueOrDefault(matchType, "[REDACTED]");
			result = result[..start] + label + result[end..];
		}

		return result;
	}

	private List<(int Start, int End, string Type)> FindRedactionTargets(string text)
	{
		var targets = new List<(int, int, string)>();

		foreach (var (name, pattern) in RedactionPatterns)
		{
			foreach (Match match in Regex.Matches(text, pattern))
			{
				targets.Add((match.Index, match.Index + match.Length, name));
			}
		}

		// Capitalized words (potential names), not at sentence start
		foreach (Match match in Regex.Matches(text, @"(?<!^)(?<!\. )\b[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*\b", RegexOptions.Multiline))
		{
			if (!SkipWords.Contains(match.Value))
			{
				targets.Add((match.Index, match.Index + match.Length, "name"));
			}
		}

		return targets;
	}
}
