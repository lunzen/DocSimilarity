using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DocGrouping.Infrastructure.TextProcessing;

public class TextNormalizer
{
	private readonly bool _removePageNumbers;
	private readonly bool _removeCommonHeaders;
	private readonly int _minWordLength;

	public TextNormalizer(
		bool removePageNumbers = true,
		bool removeCommonHeaders = true,
		int minWordLength = 1)
	{
		_removePageNumbers = removePageNumbers;
		_removeCommonHeaders = removeCommonHeaders;
		_minWordLength = minWordLength;
	}

	public string Normalize(string text)
	{
		// Step 1: Unicode normalization (handle ligatures, variants)
		text = NormalizeUnicode(text);

		// Step 2: Lowercase everything
		text = text.ToLowerInvariant();

		// Step 3: OCR error correction
		text = CorrectOcrErrors(text);

		// Step 4: Remove common document artifacts
		if (_removePageNumbers)
			text = RemovePageNumbers(text);
		if (_removeCommonHeaders)
			text = RemoveCommonArtifacts(text);

		// Step 5: Normalize whitespace
		text = NormalizeWhitespace(text);

		// Step 6: Remove/normalize punctuation
		text = NormalizePunctuation(text);

		// Step 7: Handle hyphenation (end-of-line breaks)
		text = HandleHyphenation(text);

		// Step 8: Final whitespace collapse
		text = NormalizeWhitespace(text);

		return text.Trim();
	}

	public List<string> GetTokens(string normalizedText)
	{
		return normalizedText
			.Split(' ', StringSplitOptions.RemoveEmptyEntries)
			.Where(t => t.Length >= _minWordLength)
			.ToList();
	}

	private static string NormalizeUnicode(string text)
	{
		// Use Rune API to rebuild string with only valid Unicode scalar values
		var cleaned = new System.Text.StringBuilder(text.Length);
		foreach (var rune in text.EnumerateRunes())
		{
			if (rune.Value != 0xFFFD) // skip replacement characters
				cleaned.Append(rune);
		}

		try
		{
			return cleaned.ToString().Normalize(NormalizationForm.FormKC);
		}
		catch (ArgumentException)
		{
			return cleaned.ToString();
		}
	}

	private static string CorrectOcrErrors(string text)
	{
		// Replace 0 with o in word contexts
		text = Regex.Replace(text, @"\b0([a-z])", "o$1");
		text = Regex.Replace(text, @"([a-z])0\b", "${1}o");
		text = Regex.Replace(text, @"([a-z])0([a-z])", "${1}o$2");

		// Replace 1 with l in word contexts
		text = Regex.Replace(text, @"\b1([a-z])", "l$1");
		text = Regex.Replace(text, @"([a-z])1\b", "${1}l");
		text = Regex.Replace(text, @"([a-z])1([a-z])", "${1}l$2");

		// Second pass for words with multiple errors
		text = Regex.Replace(text, @"([a-z])0([a-z])", "${1}o$2");
		text = Regex.Replace(text, @"([a-z])1([a-z])", "${1}l$2");

		// Multi-character OCR errors
		text = text.Replace("rn", "m");
		text = text.Replace("vv", "w");
		text = text.Replace("nnnn", "nn");

		return text;
	}

	private static string RemovePageNumbers(string text)
	{
		text = Regex.Replace(text, @"\bpage\s+\d+(\s+of\s+\d+)?\b", "", RegexOptions.IgnoreCase);
		text = Regex.Replace(text, @"^\s*\d+\s*$", "", RegexOptions.Multiline);
		text = Regex.Replace(text, @"-\s*\d+\s*-", "");
		return text;
	}

	private static string RemoveCommonArtifacts(string text)
	{
		// Bates stamp patterns
		text = Regex.Replace(text, @"\b[A-Z]{2,6}[-_]?\d{6,10}\b", "");

		// RECEIVED stamps
		text = Regex.Replace(text, @"\breceived\b.*?\d{1,2}[/-]\d{1,2}[/-]\d{2,4}", "", RegexOptions.IgnoreCase);

		// Fax header patterns
		text = Regex.Replace(text, @"\bfax\s+\d{3}[-.]?\d{3}[-.]?\d{4}\b", "", RegexOptions.IgnoreCase);

		// Common confidentiality footers
		text = Regex.Replace(text, @"\bconfidential\s+and\s+proprietary\b", "", RegexOptions.IgnoreCase);
		text = Regex.Replace(text, @"\bprivileged\s+and\s+confidential\b", "", RegexOptions.IgnoreCase);

		// Date stamps
		text = Regex.Replace(text, @"\b\d{1,2}[/-]\d{1,2}[/-]\d{2,4}\b", "");

		return text;
	}

	private static string NormalizeWhitespace(string text)
	{
		return Regex.Replace(text, @"\s+", " ");
	}

	private static string NormalizePunctuation(string text)
	{
		// Normalize smart quotes
		text = text.Replace("\u201C", "\"").Replace("\u201D", "\"");
		text = text.Replace("\u2018", "'").Replace("\u2019", "'");

		// Normalize dashes
		text = text.Replace("\u2014", "-").Replace("\u2013", "-");

		// Remove most punctuation except sentence-ending
		text = Regex.Replace(text, @"[,;:(){}\[\]<>""]", " ");

		return text;
	}

	private static string HandleHyphenation(string text)
	{
		return Regex.Replace(text, @"-\s+", "");
	}
}
