namespace DocGrouping.Domain.Projections;

/// <summary>
/// Lightweight projection of ungrouped documents — carries NormalizedText for Phase 3 MinHash
/// but drops OriginalText (~50% memory savings per entity).
/// </summary>
public record DocumentHashProjection(
	Guid Id,
	string TextHash,
	string FuzzyHash,
	string NormalizedText);

/// <summary>
/// Minimal hash-to-group lookup — no text at all, used for Phase 1-2 hash matching.
/// </summary>
public record DocumentGroupLookup(
	Guid DocumentId,
	string Hash,
	Guid GroupId);
