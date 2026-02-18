using System.Text.RegularExpressions;

namespace DocGrouping.Infrastructure.TextProcessing;

public class OcrErrorSimulator
{
	private static readonly Dictionary<char, char> SubstitutionMap = new()
	{
		['o'] = '0', ['0'] = 'o',
		['l'] = '1', ['1'] = 'l',
		['i'] = '1', ['I'] = '1',
		['s'] = '5', ['5'] = 's',
		['z'] = '2', ['2'] = 'z',
		['b'] = '6', ['6'] = 'b',
		['g'] = '9', ['9'] = 'g',
		['O'] = '0', ['S'] = '5',
		['Z'] = '2', ['B'] = '8',
	};

	private static readonly Dictionary<string, string> MultiCharSubstitutions = new()
	{
		["rn"] = "m", ["m"] = "rn",
		["vv"] = "w", ["w"] = "vv",
		["cl"] = "d", ["d"] = "cl",
		["nn"] = "rn", ["ii"] = "u",
	};

	private static readonly Dictionary<string, double> ErrorRates = new()
	{
		["none"] = 0.0,
		["light"] = 0.015,
		["moderate"] = 0.04,
		["heavy"] = 0.075,
	};

	private readonly Random _rng = new();

	public string ApplyErrors(string text, string errorLevel = "light")
	{
		if (errorLevel == "none" || !ErrorRates.TryGetValue(errorLevel, out var errorRate))
			return text;

		var words = Regex.Matches(text, @"\S+|\s+");
		var result = new char[text.Length + 100]; // extra buffer for multi-char subs
		var pos = 0;

		foreach (Match match in words)
		{
			var word = match.Value;
			if (word.Trim().Length > 0 && _rng.NextDouble() < errorRate)
			{
				word = ApplyErrorToWord(word);
			}
			word.CopyTo(0, result, pos, word.Length);
			pos += word.Length;
		}

		return new string(result, 0, pos);
	}

	private string ApplyErrorToWord(string word)
	{
		if (word.Length < 3 || word.All(char.IsDigit))
			return word;

		var match = Regex.Match(word, @"^(\W*)(\w+)(\W*)$");
		if (!match.Success) return word;

		var prefix = match.Groups[1].Value;
		var core = match.Groups[2].Value;
		var suffix = match.Groups[3].Value;

		if (_rng.NextDouble() < 0.2 && core.Length > 4)
			core = ApplyMultiCharSubstitution(core);
		else
			core = ApplySingleCharSubstitution(core);

		return prefix + core + suffix;
	}

	private string ApplySingleCharSubstitution(string word)
	{
		var positions = new List<int>();

		for (int i = 0; i < word.Length; i++)
		{
			if (!SubstitutionMap.ContainsKey(word[i])) continue;

			if ("01lIi".Contains(word[i]))
			{
				if (i > 0 && i < word.Length - 1 &&
					(char.IsLetter(word[i - 1]) || char.IsLetter(word[i + 1])))
				{
					positions.Add(i);
				}
			}
			else
			{
				positions.Add(i);
			}
		}

		if (positions.Count == 0) return word;

		var pos = positions[_rng.Next(positions.Count)];
		var chars = word.ToCharArray();
		chars[pos] = SubstitutionMap[chars[pos]];
		return new string(chars);
	}

	private string ApplyMultiCharSubstitution(string word)
	{
		foreach (var (pattern, replacement) in MultiCharSubstitutions)
		{
			if (word.Contains(pattern) && _rng.NextDouble() < 0.3)
			{
				var idx = word.IndexOf(pattern, StringComparison.Ordinal);
				return word[..idx] + replacement + word[(idx + pattern.Length)..];
			}
		}
		return word;
	}
}
