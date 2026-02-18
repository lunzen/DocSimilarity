using System.Security.Cryptography;
using System.Text;

namespace DocGrouping.Infrastructure.TextProcessing;

public class DocumentFingerprinter
{
	private static readonly HashSet<string> Stopwords =
	[
		"a", "an", "and", "are", "as", "at", "be", "by", "for", "from",
		"has", "he", "in", "is", "it", "its", "of", "on", "that", "the",
		"to", "was", "will", "with", "this", "but", "they", "have", "had",
		"what", "when", "where", "who", "which", "why", "how", "or", "if"
	];

	private readonly int _fuzzyTopK;
	private readonly int _fuzzyMinWordLength;

	// MinHash parameters
	private readonly int _numHashes;
	private readonly int[] _hashCoeffA;
	private readonly int[] _hashCoeffB;

	// Large prime for MinHash universal hashing
	private const int MersennePrime = 2147483647; // 2^31 - 1

	public DocumentFingerprinter(int fuzzyTopK = 50, int fuzzyMinWordLength = 6, int numHashes = 100)
	{
		_fuzzyTopK = fuzzyTopK;
		_fuzzyMinWordLength = fuzzyMinWordLength;
		_numHashes = numHashes;

		// Pre-compute deterministic hash coefficients using a fixed seed
		_hashCoeffA = new int[numHashes];
		_hashCoeffB = new int[numHashes];
		var rng = new Random(42); // deterministic seed
		for (var i = 0; i < numHashes; i++)
		{
			_hashCoeffA[i] = rng.Next(1, MersennePrime); // a must be > 0
			_hashCoeffB[i] = rng.Next(0, MersennePrime);
		}
	}

	/// <summary>
	/// FNV-1a 32-bit hash — stable across .NET runs (unlike string.GetHashCode()).
	/// </summary>
	internal static int StableStringHash(string s)
	{
		unchecked
		{
			uint hash = 2166136261;
			foreach (var c in s)
			{
				hash ^= c;
				hash *= 16777619;
			}
			return (int)(hash & 0x7FFFFFFF); // ensure non-negative
		}
	}

	public string GenerateTextHash(string normalizedText)
	{
		var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedText));
		return Convert.ToHexStringLower(bytes);
	}

	public string GenerateFuzzyHash(string normalizedText)
	{
		var topKWords = GetFuzzySignature(normalizedText);
		var signature = string.Join(" ", topKWords);
		var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(signature));
		return Convert.ToHexStringLower(bytes);
	}

	public string GenerateFileHash(byte[] fileBytes)
	{
		var bytes = SHA256.HashData(fileBytes);
		return Convert.ToHexStringLower(bytes);
	}

	public (string TextHash, string FuzzyHash) GenerateAllFingerprints(string normalizedText)
	{
		return (GenerateTextHash(normalizedText), GenerateFuzzyHash(normalizedText));
	}

	public List<string> GetFuzzySignature(string normalizedText)
	{
		var tokens = normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries);

		var filteredTokens = tokens
			.Where(t => !Stopwords.Contains(t)
				&& t.Length >= _fuzzyMinWordLength
				&& !t.Any(char.IsDigit)
				&& !t.Any(c => "$\u20AC\u00A3\u00A5%".Contains(c)))
			.ToList();

		// Fallback if too few tokens
		if (filteredTokens.Count < _fuzzyTopK / 2)
		{
			filteredTokens = tokens
				.Where(t => t.Length >= _fuzzyMinWordLength && !t.Any(char.IsDigit))
				.ToList();
		}

		// Count word frequencies
		var wordCounts = filteredTokens
			.GroupBy(t => t)
			.ToDictionary(g => g.Key, g => g.Count());

		// Get top K most common words, sorted alphabetically
		var topKWords = wordCounts
			.OrderByDescending(kv => kv.Value)
			.ThenBy(kv => kv.Key)
			.Take(_fuzzyTopK)
			.Select(kv => kv.Key)
			.Order()
			.ToList();

		return topKWords;
	}

	/// <summary>
	/// Generates a MinHash signature for the given normalized text.
	/// Tokenizes text into a word set, then computes _numHashes min-hash values.
	/// </summary>
	public int[] GenerateMinHashSignature(string normalizedText)
	{
		var tokens = new HashSet<string>(normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries));
		return GenerateMinHashSignature(tokens);
	}

	/// <summary>
	/// Generates a MinHash signature from a pre-computed token set.
	/// For each of _numHashes hash functions, takes the minimum hash across all tokens.
	/// </summary>
	public int[] GenerateMinHashSignature(HashSet<string> tokenSet)
	{
		var signature = new int[_numHashes];
		Array.Fill(signature, int.MaxValue);

		foreach (var token in tokenSet)
		{
			var tokenHash = StableStringHash(token);
			for (var i = 0; i < _numHashes; i++)
			{
				// Universal hash: h_i(x) = (a_i * x + b_i) mod p
				var h = (int)(((long)_hashCoeffA[i] * tokenHash + _hashCoeffB[i]) % MersennePrime);
				if (h < signature[i])
					signature[i] = h;
			}
		}

		return signature;
	}

	public SimilarityMetrics CalculateSimilarityMetrics(string text1, string text2)
	{
		var tokens1 = new HashSet<string>(text1.Split(' ', StringSplitOptions.RemoveEmptyEntries));
		var tokens2 = new HashSet<string>(text2.Split(' ', StringSplitOptions.RemoveEmptyEntries));

		var intersection = new HashSet<string>(tokens1);
		intersection.IntersectWith(tokens2);

		var union = new HashSet<string>(tokens1);
		union.UnionWith(tokens2);

		var jaccard = union.Count > 0 ? (double)intersection.Count / union.Count : 0;
		var overlap = tokens1.Count > 0 && tokens2.Count > 0
			? (double)intersection.Count / Math.Min(tokens1.Count, tokens2.Count)
			: 0;

		var sig1 = new HashSet<string>(GetFuzzySignature(text1));
		var sig2 = new HashSet<string>(GetFuzzySignature(text2));
		var sigIntersection = new HashSet<string>(sig1);
		sigIntersection.IntersectWith(sig2);
		var sigUnion = new HashSet<string>(sig1);
		sigUnion.UnionWith(sig2);
		var sigJaccard = sigUnion.Count > 0 ? (double)sigIntersection.Count / sigUnion.Count : 0;

		return new SimilarityMetrics
		{
			JaccardSimilarity = jaccard,
			OverlapCoefficient = overlap,
			FuzzySignatureJaccard = sigJaccard,
			TokenCount1 = tokens1.Count,
			TokenCount2 = tokens2.Count,
			CommonTokens = intersection.Count
		};
	}
}

public class SimilarityMetrics
{
	public double JaccardSimilarity { get; set; }
	public double OverlapCoefficient { get; set; }
	public double FuzzySignatureJaccard { get; set; }
	public int TokenCount1 { get; set; }
	public int TokenCount2 { get; set; }
	public int CommonTokens { get; set; }
}
