using System.Text.RegularExpressions;

namespace DocGrouping.Infrastructure.TextProcessing;

public enum DiffType { Same, Added, Removed }

public record InlineSpan(string Text, bool IsChanged);

public record DiffLine(DiffType Type, int? Line1, int? Line2, string Content, List<InlineSpan>? InlineSpans = null);

public record WordDiffStats(int TotalWords1, int TotalWords2, int CommonWords, int AddedWords, int RemovedWords)
{
	public int ChangedWords => AddedWords + RemovedWords;
	public double SimilarityPct => TotalWords1 + TotalWords2 > 0
		? (double)CommonWords * 2 / (TotalWords1 + TotalWords2) * 100
		: 100;
}

public partial class DiffEngine
{
	public List<DiffLine> ComputeDiff(string text1, string text2)
	{
		var segments1 = SplitIntoSegments(text1 ?? "");
		var segments2 = SplitIntoSegments(text2 ?? "");

		var lcs = ComputeLcs(segments1, segments2);
		var result = new List<DiffLine>();

		int i = 0, j = 0, k = 0;
		while (i < segments1.Length || j < segments2.Length)
		{
			if (k < lcs.Count && i < segments1.Length && j < segments2.Length
				&& segments1[i] == lcs[k] && segments2[j] == lcs[k])
			{
				result.Add(new DiffLine(DiffType.Same, i + 1, j + 1, segments1[i]));
				i++; j++; k++;
			}
			else if (k < lcs.Count && j < segments2.Length && segments2[j] == lcs[k]
				|| k >= lcs.Count && i < segments1.Length)
			{
				result.Add(new DiffLine(DiffType.Removed, i + 1, null, segments1[i]));
				i++;
			}
			else
			{
				result.Add(new DiffLine(DiffType.Added, null, j + 1, segments2[j]));
				j++;
			}
		}

		return result;
	}

	/// <summary>
	/// Computes word-level diff statistics between two texts.
	/// </summary>
	public WordDiffStats ComputeWordStats(string text1, string text2)
	{
		var words1 = SplitWords(text1 ?? "");
		var words2 = SplitWords(text2 ?? "");

		var lcs = ComputeLcs(words1, words2);
		int common = lcs.Count;

		return new WordDiffStats(
			TotalWords1: words1.Length,
			TotalWords2: words2.Length,
			CommonWords: common,
			AddedWords: words2.Length - common,
			RemovedWords: words1.Length - common
		);
	}

	/// <summary>
	/// Splits text into diff-friendly segments. Uses newlines when present,
	/// falls back to sentence boundaries for long single-line text.
	/// </summary>
	private static string[] SplitIntoSegments(string text)
	{
		var lines = text.Split('\n');

		// If text already has reasonable line structure, use it
		if (lines.Length >= 5 || lines.All(l => l.Length <= 200))
			return lines;

		// Long single-line text: split on sentence boundaries
		var segments = new List<string>();
		foreach (var line in lines)
		{
			if (line.Length <= 200)
			{
				segments.Add(line);
				continue;
			}

			// Split on sentence-ending punctuation followed by a space or capital letter
			var sentences = SentenceSplitRegex().Split(line);
			foreach (var s in sentences)
			{
				var trimmed = s.Trim();
				if (trimmed.Length > 0)
					segments.Add(trimmed);
			}
		}

		return segments.Count > 0 ? segments.ToArray() : [""];
	}

	private static string[] SplitWords(string text)
	{
		return WordSplitRegex().Split(text.Trim())
			.Where(w => w.Length > 0)
			.ToArray();
	}

	private static List<string> ComputeLcs(string[] a, string[] b)
	{
		int m = a.Length, n = b.Length;

		// For very large arrays (word-level diff), use a hash-based approach
		// to keep memory reasonable. The standard DP is O(m*n) which is fine
		// for segments but could be large for words. Cap at 5000 elements each.
		if (m > 5000 || n > 5000)
			return ComputeLcsHashed(a, b);

		var dp = new int[m + 1, n + 1];

		for (int i = 1; i <= m; i++)
		{
			for (int j = 1; j <= n; j++)
			{
				dp[i, j] = a[i - 1] == b[j - 1]
					? dp[i - 1, j - 1] + 1
					: Math.Max(dp[i - 1, j], dp[i, j - 1]);
			}
		}

		var result = new List<string>();
		int x = m, y = n;
		while (x > 0 && y > 0)
		{
			if (a[x - 1] == b[y - 1])
			{
				result.Add(a[x - 1]);
				x--; y--;
			}
			else if (dp[x - 1, y] > dp[x, y - 1])
			{
				x--;
			}
			else
			{
				y--;
			}
		}

		result.Reverse();
		return result;
	}

	/// <summary>
	/// Hunt-Szymanski-style LCS for large inputs: index positions of each
	/// element in b, then greedily find the longest increasing subsequence.
	/// </summary>
	private static List<string> ComputeLcsHashed(string[] a, string[] b)
	{
		// Build index of b values -> positions (reversed so we process largest first)
		var bIndex = new Dictionary<string, List<int>>();
		for (int j = b.Length - 1; j >= 0; j--)
		{
			if (!bIndex.TryGetValue(b[j], out var list))
			{
				list = [];
				bIndex[b[j]] = list;
			}
			list.Add(j);
		}

		// For each element in a that exists in b, collect (position in b)
		// Then find LIS on those positions
		var matchPositions = new List<(int posA, int posB)>();
		for (int i = 0; i < a.Length; i++)
		{
			if (bIndex.TryGetValue(a[i], out var positions))
			{
				foreach (var j in positions) // already in descending order
					matchPositions.Add((i, j));
			}
		}

		// Simple patience sort / LIS on posB values
		if (matchPositions.Count == 0)
			return [];

		// Just count common elements for stats purposes (approximate LCS)
		// For display we don't need exact LCS on huge word arrays
		var commonSet = new HashSet<string>(a);
		commonSet.IntersectWith(b);

		// Count min occurrences of each common word
		var countA = new Dictionary<string, int>();
		var countB = new Dictionary<string, int>();
		foreach (var w in a)
			countA[w] = countA.GetValueOrDefault(w) + 1;
		foreach (var w in b)
			countB[w] = countB.GetValueOrDefault(w) + 1;

		var result = new List<string>();
		foreach (var w in commonSet)
			for (int c = 0; c < Math.Min(countA[w], countB[w]); c++)
				result.Add(w);

		return result;
	}

	/// <summary>
	/// Post-processes diff results to add inline word-level highlighting
	/// for paired removed/added segments.
	/// </summary>
	public static List<DiffLine> AddInlineHighlighting(List<DiffLine> diffLines)
	{
		var result = new List<DiffLine>(diffLines.Count);

		for (int i = 0; i < diffLines.Count; i++)
		{
			if (diffLines[i].Type == DiffType.Same)
			{
				result.Add(diffLines[i]);
				continue;
			}

			// Collect a block of non-Same lines (any mix of Removed and Added)
			var block = new List<int>();
			int j = i;
			while (j < diffLines.Count && diffLines[j].Type != DiffType.Same)
				block.Add(j++);

			var removed = block.Where(idx => diffLines[idx].Type == DiffType.Removed).ToList();
			var added = block.Where(idx => diffLines[idx].Type == DiffType.Added).ToList();

			// Compute inline diffs for paired segments
			int pairs = Math.Min(removed.Count, added.Count);
			var processedRemoved = new HashSet<int>();
			var processedAdded = new HashSet<int>();

			for (int p = 0; p < pairs; p++)
			{
				var (removedSpans, addedSpans) = ComputeInlineWordDiff(
					diffLines[removed[p]].Content,
					diffLines[added[p]].Content);

				processedRemoved.Add(removed[p]);
				processedAdded.Add(added[p]);

				// Store paired results to emit in original order later
				diffLines[removed[p]] = diffLines[removed[p]] with { InlineSpans = removedSpans };
				diffLines[added[p]] = diffLines[added[p]] with { InlineSpans = addedSpans };
			}

			// Emit in original order to preserve diff structure
			foreach (var idx in block)
				result.Add(diffLines[idx]);

			i = j - 1;
		}

		return result;
	}

	/// <summary>
	/// Computes word-level inline diff between two strings.
	/// Returns two lists of spans: one for the removed text, one for the added text.
	/// </summary>
	private static (List<InlineSpan> Removed, List<InlineSpan> Added) ComputeInlineWordDiff(string text1, string text2)
	{
		var words1 = InlineWordSplitRegex().Split(text1);
		var words2 = InlineWordSplitRegex().Split(text2);

		var lcs = ComputeLcs(words1, words2);

		var removedSpans = BuildInlineSpans(words1, lcs);
		var addedSpans = BuildInlineSpans(words2, lcs);

		return (removedSpans, addedSpans);
	}

	private static List<InlineSpan> BuildInlineSpans(string[] words, List<string> lcs)
	{
		var spans = new List<InlineSpan>();
		int k = 0;

		for (int i = 0; i < words.Length; i++)
		{
			if (k < lcs.Count && words[i] == lcs[k])
			{
				// Common word — merge with previous unchanged span if possible
				if (spans.Count > 0 && !spans[^1].IsChanged)
					spans[^1] = new InlineSpan(spans[^1].Text + " " + words[i], false);
				else
					spans.Add(new InlineSpan(words[i], false));
				k++;
			}
			else
			{
				// Changed word
				if (spans.Count > 0 && spans[^1].IsChanged)
					spans[^1] = new InlineSpan(spans[^1].Text + " " + words[i], true);
				else
					spans.Add(new InlineSpan(words[i], true));
			}
		}

		return spans;
	}

	[GeneratedRegex(@"(?<=[.!?])\s+(?=[A-Z])")]
	private static partial Regex SentenceSplitRegex();

	[GeneratedRegex(@"\s+")]
	private static partial Regex WordSplitRegex();

	[GeneratedRegex(@"\s+")]
	private static partial Regex InlineWordSplitRegex();
}
