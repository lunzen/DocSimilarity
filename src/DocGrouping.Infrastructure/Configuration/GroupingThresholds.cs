namespace DocGrouping.Infrastructure.Configuration;

/// <summary>
/// Configurable thresholds that control how documents are assigned to confidence tiers
/// during the grouping process. Bound from appsettings.json "GroupingThresholds" section.
/// </summary>
public class GroupingThresholds
{
	/// <summary>
	/// Minimum Jaccard similarity required to group two documents together.
	/// Documents below this threshold become singletons (None confidence).
	/// Default: 0.70 (70%)
	/// </summary>
	public double MediumMinJaccard { get; set; } = 0.70;

	/// <summary>
	/// Upper bound of the Medium tier. Jaccard scores at or above this value
	/// are classified as High confidence (when matched via similarity, not hash).
	/// Default: 0.85 (85%)
	/// </summary>
	public double HighMinJaccard { get; set; } = 0.85;

	/// <summary>
	/// The similarity score recorded for fuzzy hash matches. Fuzzy hash matches
	/// are always classified as High confidence.
	/// Default: 0.90
	/// </summary>
	public double FuzzyHashAssumedSimilarity { get; set; } = 0.90;

	/// <summary>
	/// MinHash pre-filter threshold for the LSH candidate verification step.
	/// Candidates with estimated Jaccard below this are skipped without loading full text.
	/// Should be slightly below MediumMinJaccard to avoid missing edge-case matches.
	/// Default: 0.50
	/// </summary>
	public double MinHashPrefilterThreshold { get; set; } = 0.50;
}
