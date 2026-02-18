namespace DocGrouping.Infrastructure.TextProcessing;

/// <summary>
/// Lightweight in-memory Locality-Sensitive Hashing index using MinHash banding.
/// Not persisted to DB — rebuilt each time Phase 3 runs.
///
/// Parameters: b=20 bands, r=5 rows per band, n=100 total hashes.
/// S-curve threshold ≈ 0.55, P(candidate | J=0.70) ≈ 97.5%, P(candidate | J=0.30) ≈ 4.7%.
/// </summary>
public class MinHashLshIndex
{
	private readonly int _bands;
	private readonly int _rowsPerBand;

	// buckets[bandIndex] -> dictionary of (bucketHash -> list of doc indices)
	private readonly Dictionary<int, List<int>>[] _buckets;

	/// <summary>Maximum bucket size before it's considered degenerate and skipped.</summary>
	private const int MaxBucketSize = 100;

	public MinHashLshIndex(int bands = 20, int rowsPerBand = 5)
	{
		_bands = bands;
		_rowsPerBand = rowsPerBand;
		_buckets = new Dictionary<int, List<int>>[bands];
		for (var i = 0; i < bands; i++)
			_buckets[i] = new Dictionary<int, List<int>>();
	}

	/// <summary>
	/// Inserts a document's MinHash signature into the index.
	/// For each of the 20 bands, hashes the 5-row slice and buckets it.
	/// </summary>
	public void Add(int docIndex, int[] signature)
	{
		for (var band = 0; band < _bands; band++)
		{
			var bucketHash = HashBand(signature, band * _rowsPerBand, _rowsPerBand);
			if (!_buckets[band].TryGetValue(bucketHash, out var bucket))
			{
				bucket = [];
				_buckets[band][bucketHash] = bucket;
			}
			bucket.Add(docIndex);
		}
	}

	/// <summary>
	/// Returns all candidate pairs that share at least one LSH bucket.
	/// Skips degenerate buckets with more than 100 entries.
	/// </summary>
	public HashSet<(int, int)> GetCandidatePairs()
	{
		var pairs = new HashSet<(int, int)>();

		for (var band = 0; band < _bands; band++)
		{
			foreach (var bucket in _buckets[band].Values)
			{
				if (bucket.Count < 2 || bucket.Count > MaxBucketSize)
					continue;

				for (var i = 0; i < bucket.Count; i++)
				{
					for (var j = i + 1; j < bucket.Count; j++)
					{
						var a = bucket[i];
						var b = bucket[j];
						// Canonical ordering so (a,b) == (b,a)
						pairs.Add(a < b ? (a, b) : (b, a));
					}
				}
			}
		}

		return pairs;
	}

	/// <summary>
	/// Hashes a band slice of the signature using FNV-1a to produce a bucket key.
	/// </summary>
	private static int HashBand(int[] signature, int offset, int length)
	{
		unchecked
		{
			uint hash = 2166136261;
			for (var i = offset; i < offset + length; i++)
			{
				// Mix each int's 4 bytes into the hash
				var val = (uint)signature[i];
				hash ^= val & 0xFF; hash *= 16777619;
				hash ^= (val >> 8) & 0xFF; hash *= 16777619;
				hash ^= (val >> 16) & 0xFF; hash *= 16777619;
				hash ^= (val >> 24) & 0xFF; hash *= 16777619;
			}
			return (int)hash;
		}
	}
}
