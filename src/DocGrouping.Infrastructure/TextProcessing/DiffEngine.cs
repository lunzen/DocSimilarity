namespace DocGrouping.Infrastructure.TextProcessing;

public enum DiffType { Same, Added, Removed }

public record DiffLine(DiffType Type, int? Line1, int? Line2, string Content);

public class DiffEngine
{
	public List<DiffLine> ComputeDiff(string text1, string text2)
	{
		var lines1 = (text1 ?? "").Split('\n');
		var lines2 = (text2 ?? "").Split('\n');

		var lcs = ComputeLcs(lines1, lines2);
		var result = new List<DiffLine>();

		int i = 0, j = 0, k = 0;
		while (i < lines1.Length || j < lines2.Length)
		{
			if (k < lcs.Count && i < lines1.Length && j < lines2.Length
				&& lines1[i] == lcs[k] && lines2[j] == lcs[k])
			{
				result.Add(new DiffLine(DiffType.Same, i + 1, j + 1, lines1[i]));
				i++; j++; k++;
			}
			else if (k < lcs.Count && j < lines2.Length && lines2[j] == lcs[k]
				|| k >= lcs.Count && i < lines1.Length)
			{
				result.Add(new DiffLine(DiffType.Removed, i + 1, null, lines1[i]));
				i++;
			}
			else
			{
				result.Add(new DiffLine(DiffType.Added, null, j + 1, lines2[j]));
				j++;
			}
		}

		return result;
	}

	private static List<string> ComputeLcs(string[] a, string[] b)
	{
		int m = a.Length, n = b.Length;
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
}
