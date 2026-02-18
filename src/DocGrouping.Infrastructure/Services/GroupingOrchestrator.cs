using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using DocGrouping.Application.DTOs;
using DocGrouping.Application.Interfaces;
using DocGrouping.Domain.Entities;
using DocGrouping.Domain.Enums;
using DocGrouping.Domain.Interfaces;
using DocGrouping.Infrastructure.Rules;
using DocGrouping.Infrastructure.TextProcessing;
using Microsoft.Extensions.Logging;

namespace DocGrouping.Infrastructure.Services;

public class GroupingOrchestrator(
	IDocumentRepository documentRepository,
	IDocumentGroupRepository groupRepository,
	DocumentFingerprinter fingerprinter,
	RulesEngine rulesEngine,
	ILogger<GroupingOrchestrator> logger) : IGroupingOrchestrator
{
	public Task<List<DocumentGroup>> GroupAllDocumentsAsync(CancellationToken ct = default)
		=> GroupAllDocumentsAsync(null!, ct);

	public async Task<List<DocumentGroup>> GroupAllDocumentsAsync(IProgress<GroupingProgress>? progress, CancellationToken ct = default)
	{
		var totalSw = Stopwatch.StartNew();
		var phaseSw = new Stopwatch();
		var metrics = new GroupingMetrics();

		logger.LogInformation("Starting full grouping of all documents");
		progress?.Report(new GroupingProgress("Init", "Clearing existing groups...", 2));

		// Clear existing groups
		await groupRepository.DeleteAllAsync(ct);

		phaseSw.Restart();
		var documents = await documentRepository.GetAllAsync(ct);
		var loadTime = phaseSw.Elapsed.TotalSeconds;

		if (documents.Count == 0)
		{
			logger.LogWarning("No documents to group");
			return [];
		}

		metrics.TotalDocuments = documents.Count;
		logger.LogInformation("Loaded {Count} documents in {Time:F2}s", documents.Count, loadTime);
		progress?.Report(new GroupingProgress("Init", $"Loaded {documents.Count} documents from DB in {loadTime:F1}s", 5));

		var grouped = new HashSet<Guid>();
		var groups = new List<DocumentGroup>();
		var nextGroupNumber = 1;

		// ── Phase 1: Group by text hash (VERY_HIGH confidence) ──
		phaseSw.Restart();
		progress?.Report(new GroupingProgress("Phase 1", "Building text hash index...", 8));

		var textHashIndex = documents
			.GroupBy(d => d.TextHash)
			.Where(g => g.Count() > 1)
			.ToDictionary(g => g.Key, g => g.ToList());

		progress?.Report(new GroupingProgress("Phase 1", $"Found {textHashIndex.Count} hash collisions, evaluating rules...", 10));

		foreach (var (textHash, matchingDocs) in textHashIndex)
		{
			var shouldGroup = true;
			var ruleExplanations = new List<string>();

			for (var i = 0; i < matchingDocs.Count && shouldGroup; i++)
			{
				for (var j = i + 1; j < matchingDocs.Count && shouldGroup; j++)
				{
					var decision = rulesEngine.ShouldGroup(
						matchingDocs[i].OriginalText, GetMetadata(matchingDocs[i]),
						matchingDocs[j].OriginalText, GetMetadata(matchingDocs[j]),
						"very_high");

					if (!decision.ShouldGroup)
					{
						shouldGroup = false;
						ruleExplanations.AddRange(decision.Explanation);
					}
				}
			}

			if (!shouldGroup) continue;

			var canonicalIndex = SelectCanonical(matchingDocs);
			var group = CreateGroup(
				nextGroupNumber++,
				matchingDocs,
				MatchConfidence.VeryHigh,
				"Exact text match (normalized)" +
					(ruleExplanations.Count > 0 ? $" | Rules: {string.Join("; ", ruleExplanations)}" : ""),
				matchingDocs[canonicalIndex],
				1.0m);

			groups.Add(group);
			await groupRepository.AddAsync(group, ct);
			grouped.UnionWith(matchingDocs.Select(d => d.Id));
		}

		metrics.Phase1Seconds = phaseSw.Elapsed.TotalSeconds;
		metrics.Phase1Groups = groups.Count;
		var p1Docs = grouped.Count;
		logger.LogInformation("Phase 1 (text hash): {Groups} groups, {Docs} docs grouped in {Time:F2}s",
			groups.Count, p1Docs, metrics.Phase1Seconds);
		progress?.Report(new GroupingProgress("Phase 1",
			$"Done: {groups.Count} groups ({p1Docs} docs) in {metrics.Phase1Seconds:F1}s", 25));

		// ── Phase 2: Group by fuzzy hash (HIGH confidence) ──
		phaseSw.Restart();
		var fuzzyHashIndex = documents
			.Where(d => !grouped.Contains(d.Id))
			.GroupBy(d => d.FuzzyHash)
			.Where(g => g.Count() > 1)
			.ToDictionary(g => g.Key, g => g.ToList());

		progress?.Report(new GroupingProgress("Phase 2",
			$"Found {fuzzyHashIndex.Count} fuzzy hash collisions among {documents.Count - grouped.Count} remaining docs...", 30));

		var phase2Count = 0;
		foreach (var (fuzzyHash, matchingDocs) in fuzzyHashIndex)
		{
			var ungrouped = matchingDocs.Where(d => !grouped.Contains(d.Id)).ToList();
			if (ungrouped.Count <= 1) continue;

			var group = CreateGroup(
				nextGroupNumber++,
				ungrouped,
				MatchConfidence.High,
				"Fuzzy content match (top-K tokens)",
				ungrouped[0],
				0.9m);

			groups.Add(group);
			await groupRepository.AddAsync(group, ct);
			grouped.UnionWith(ungrouped.Select(d => d.Id));
			phase2Count++;
		}

		metrics.Phase2Seconds = phaseSw.Elapsed.TotalSeconds;
		metrics.Phase2Groups = phase2Count;
		logger.LogInformation("Phase 2 (fuzzy hash): {Groups} groups in {Time:F2}s",
			phase2Count, metrics.Phase2Seconds);
		progress?.Report(new GroupingProgress("Phase 2",
			$"Done: {phase2Count} groups in {metrics.Phase2Seconds:F1}s — {grouped.Count}/{documents.Count} docs grouped so far", 45));

		// ── Phase 3: Similarity grouping (MEDIUM confidence, 70-85% Jaccard) ──
		// Uses MinHash + LSH for candidate generation when ≥200 docs, brute-force otherwise.
		phaseSw.Restart();
		var ungroupedDocs = documents.Where(d => !grouped.Contains(d.Id)).ToList();
		metrics.Phase3UngroupedCount = ungroupedDocs.Count;
		var totalPairs = (long)ungroupedDocs.Count * (ungroupedDocs.Count - 1) / 2;

		logger.LogInformation("Phase 3: {Count} ungrouped docs, {Pairs:N0} total possible pairs",
			ungroupedDocs.Count, totalPairs);
		progress?.Report(new GroupingProgress("Phase 3",
			$"Analyzing {ungroupedDocs.Count} ungrouped docs...", 48));

		var verifiedPairs = new ConcurrentBag<(int I, int J)>();
		long candidatePairCount = 0;
		long pairsCompared = 0;

		if (ungroupedDocs.Count < 200)
		{
			// ── Brute-force path for small sets (LSH overhead not worthwhile) ──
			logger.LogInformation("Phase 3: Using brute-force for {Count} docs (<200 threshold)", ungroupedDocs.Count);
			candidatePairCount = totalPairs;

			Parallel.ForEach(
				Partitioner.Create(Enumerable.Range(0, ungroupedDocs.Count), EnumerablePartitionerOptions.NoBuffering),
				i =>
			{
				for (var j = i + 1; j < ungroupedDocs.Count; j++)
				{
					Interlocked.Increment(ref pairsCompared);
					var simMetrics = fingerprinter.CalculateSimilarityMetrics(
						ungroupedDocs[i].NormalizedText,
						ungroupedDocs[j].NormalizedText);

					if (simMetrics.JaccardSimilarity >= 0.70 && simMetrics.JaccardSimilarity <= 0.85)
						verifiedPairs.Add((i, j));
				}
			});
		}
		else
		{
			// ── LSH path: MinHash signatures → banding → candidate pairs → exact verification ──

			// Step 1: Compute MinHash signatures in parallel
			progress?.Report(new GroupingProgress("Phase 3",
				$"Computing MinHash signatures for {ungroupedDocs.Count} docs...", 50));

			var signatures = new int[ungroupedDocs.Count][];
			Parallel.For(0, ungroupedDocs.Count, i =>
			{
				signatures[i] = fingerprinter.GenerateMinHashSignature(ungroupedDocs[i].NormalizedText);
			});

			// Step 2: Build LSH index
			progress?.Report(new GroupingProgress("Phase 3", "Building LSH index...", 55));
			var lshIndex = new MinHashLshIndex(bands: 20, rowsPerBand: 5);
			for (var i = 0; i < signatures.Length; i++)
				lshIndex.Add(i, signatures[i]);

			// Step 3: Extract candidate pairs
			var candidatePairs = lshIndex.GetCandidatePairs();
			candidatePairCount = candidatePairs.Count;
			var reductionPct = totalPairs > 0 ? 100.0 * (1 - (double)candidatePairCount / totalPairs) : 0;

			logger.LogInformation(
				"Phase 3 LSH: {Candidates:N0} candidate pairs from {Total:N0} possible ({Reduction:F1}% reduction)",
				candidatePairCount, totalPairs, reductionPct);
			progress?.Report(new GroupingProgress("Phase 3",
				$"Verifying {candidatePairCount:N0} candidate pairs ({reductionPct:F1}% reduction from LSH)...", 60));

			// Step 4: Verify candidates with exact Jaccard
			var lastReport = Stopwatch.GetTimestamp();
			Parallel.ForEach(candidatePairs, pair =>
			{
				var count = Interlocked.Increment(ref pairsCompared);

				// Throttled progress reporting
				if (count % 5000 == 0 && progress != null)
				{
					var now = Stopwatch.GetTimestamp();
					if (Stopwatch.GetElapsedTime(Interlocked.Read(ref lastReport), now).TotalSeconds >= 1.0)
					{
						Interlocked.Exchange(ref lastReport, now);
						var pct = 60 + (int)(15.0 * count / candidatePairCount);
						progress.Report(new GroupingProgress("Phase 3",
							$"Verifying {count:N0}/{candidatePairCount:N0} candidate pairs...", pct));
					}
				}

				var simMetrics = fingerprinter.CalculateSimilarityMetrics(
					ungroupedDocs[pair.Item1].NormalizedText,
					ungroupedDocs[pair.Item2].NormalizedText);

				if (simMetrics.JaccardSimilarity >= 0.70 && simMetrics.JaccardSimilarity <= 0.85)
					verifiedPairs.Add((pair.Item1, pair.Item2));
			});
		}

		// Sequential union-find merge on verified pairs
		var similarityGroups = new Dictionary<int, List<Document>>();
		var docToGroup = new Dictionary<Guid, int>();
		var tempGroupId = 0;

		foreach (var (i, j) in verifiedPairs)
		{
			var id1 = ungroupedDocs[i].Id;
			var id2 = ungroupedDocs[j].Id;

			if (docToGroup.TryGetValue(id1, out var gid1) && docToGroup.TryGetValue(id2, out var gid2))
			{
				if (gid1 != gid2)
				{
					similarityGroups[gid1].AddRange(similarityGroups[gid2]);
					foreach (var doc in similarityGroups[gid2])
						docToGroup[doc.Id] = gid1;
					similarityGroups.Remove(gid2);
				}
			}
			else if (docToGroup.TryGetValue(id1, out var existingGroup))
			{
				similarityGroups[existingGroup].Add(ungroupedDocs[j]);
				docToGroup[id2] = existingGroup;
			}
			else if (docToGroup.TryGetValue(id2, out existingGroup))
			{
				similarityGroups[existingGroup].Add(ungroupedDocs[i]);
				docToGroup[id1] = existingGroup;
			}
			else
			{
				similarityGroups[tempGroupId] = [ungroupedDocs[i], ungroupedDocs[j]];
				docToGroup[id1] = tempGroupId;
				docToGroup[id2] = tempGroupId;
				tempGroupId++;
			}
		}

		metrics.Phase3PairsCompared = pairsCompared;
		metrics.Phase3CandidatePairs = candidatePairCount;
		metrics.Phase3VerifiedPairs = verifiedPairs.Count;

		var phase3Count = 0;
		foreach (var (_, docsInGroup) in similarityGroups)
		{
			if (docsInGroup.Count <= 1) continue;

			var group = CreateGroup(
				nextGroupNumber++,
				docsInGroup,
				MatchConfidence.Medium,
				"Moderate content similarity (70-85% Jaccard)",
				docsInGroup[0],
				0.75m);

			groups.Add(group);
			await groupRepository.AddAsync(group, ct);
			grouped.UnionWith(docsInGroup.Select(d => d.Id));
			phase3Count++;
		}

		metrics.Phase3Seconds = phaseSw.Elapsed.TotalSeconds;
		metrics.Phase3Groups = phase3Count;
		var pairRate = pairsCompared / Math.Max(metrics.Phase3Seconds, 0.001);
		logger.LogInformation(
			"Phase 3 (similarity): {Groups} groups | {Candidates:N0} candidates, {Verified:N0} verified, {Compared:N0} compared in {Time:F2}s ({Rate:N0} pairs/sec)",
			phase3Count, candidatePairCount, verifiedPairs.Count, pairsCompared, metrics.Phase3Seconds, pairRate);
		progress?.Report(new GroupingProgress("Phase 3",
			$"Done: {phase3Count} groups | {candidatePairCount:N0} candidates → {verifiedPairs.Count:N0} verified in {metrics.Phase3Seconds:F1}s", 78));

		// ── Phase 4: Singleton groups for unmatched documents ──
		phaseSw.Restart();
		var ungroupedForSingleton = documents.Where(d => !grouped.Contains(d.Id)).ToList();
		progress?.Report(new GroupingProgress("Phase 4",
			$"Creating singleton groups for {ungroupedForSingleton.Count} unmatched docs...", 80));

		var singletonCount = 0;
		foreach (var doc in ungroupedForSingleton)
		{
			var group = CreateGroup(
				nextGroupNumber++,
				[doc],
				MatchConfidence.None,
				"No matches found (unique document)",
				doc,
				0m);

			groups.Add(group);
			await groupRepository.AddAsync(group, ct);
			singletonCount++;

			if (singletonCount % 100 == 0)
			{
				var pct = 80 + (int)(18.0 * singletonCount / ungroupedForSingleton.Count);
				progress?.Report(new GroupingProgress("Phase 4",
					$"Creating singletons... {singletonCount}/{ungroupedForSingleton.Count}", pct));
			}
		}

		metrics.Phase4Seconds = phaseSw.Elapsed.TotalSeconds;
		metrics.Phase4Singletons = singletonCount;
		metrics.TotalSeconds = totalSw.Elapsed.TotalSeconds;
		metrics.TotalGroups = groups.Count;

		logger.LogInformation("Phase 4 (singletons): {Count} groups in {Time:F2}s",
			singletonCount, metrics.Phase4Seconds);
		logger.LogInformation(
			"Grouping complete: {TotalGroups} groups from {TotalDocs} documents in {Time:F2}s " +
			"[P1={P1:F1}s P2={P2:F1}s P3={P3:F1}s({Pairs:N0}pairs) P4={P4:F1}s]",
			groups.Count, documents.Count, metrics.TotalSeconds,
			metrics.Phase1Seconds, metrics.Phase2Seconds, metrics.Phase3Seconds,
			metrics.Phase3PairsCompared, metrics.Phase4Seconds);

		progress?.Report(new GroupingProgress("Done",
			$"Complete: {groups.Count} groups from {documents.Count} docs in {metrics.TotalSeconds:F1}s",
			100, metrics));

		return groups;
	}

	public async Task<DocumentGroup> GroupSingleDocumentAsync(Document document, CancellationToken ct = default)
	{
		// Phase 1: Check text hash
		var textHashMatch = await documentRepository.GetByTextHashAsync(document.TextHash, ct);
		if (textHashMatch != null && textHashMatch.Id != document.Id && textHashMatch.GroupMembership != null)
		{
			var existingGroup = await groupRepository.GetByIdAsync(textHashMatch.GroupMembership.GroupId, ct);
			if (existingGroup != null)
			{
				AddToGroup(existingGroup, document, 1.0m);
				await groupRepository.UpdateAsync(existingGroup, ct);
				return existingGroup;
			}
		}

		// Phase 2: Check fuzzy hash
		var fuzzyHashMatch = await documentRepository.GetByFuzzyHashAsync(document.FuzzyHash, ct);
		if (fuzzyHashMatch != null && fuzzyHashMatch.Id != document.Id && fuzzyHashMatch.GroupMembership != null)
		{
			var existingGroup = await groupRepository.GetByIdAsync(fuzzyHashMatch.GroupMembership.GroupId, ct);
			if (existingGroup != null)
			{
				existingGroup.Confidence = MatchConfidence.High;
				existingGroup.MatchReason = "Fuzzy content match (top-K tokens)";
				AddToGroup(existingGroup, document, 0.9m);
				await groupRepository.UpdateAsync(existingGroup, ct);
				return existingGroup;
			}
		}

		// Phase 3: Pairwise Jaccard against all ungrouped (will be replaced by LSH in Phase 3)
		var allDocs = await documentRepository.GetAllAsync(ct);
		foreach (var candidate in allDocs.Where(d => d.Id != document.Id))
		{
			var metrics = fingerprinter.CalculateSimilarityMetrics(
				document.NormalizedText, candidate.NormalizedText);

			if (metrics.JaccardSimilarity >= 0.70 && metrics.JaccardSimilarity <= 0.85
				&& candidate.GroupMembership != null)
			{
				var existingGroup = await groupRepository.GetByIdAsync(candidate.GroupMembership.GroupId, ct);
				if (existingGroup != null)
				{
					existingGroup.Confidence = MatchConfidence.Medium;
					existingGroup.MatchReason = "Moderate content similarity (70-85% Jaccard)";
					AddToGroup(existingGroup, document, (decimal)metrics.JaccardSimilarity);
					await groupRepository.UpdateAsync(existingGroup, ct);
					return existingGroup;
				}
			}
		}

		// Phase 4: Create singleton group
		var nextGroupNumber = await groupRepository.GetNextGroupNumberAsync(ct);
		var singletonGroup = CreateGroup(
			nextGroupNumber,
			[document],
			MatchConfidence.None,
			"No matches found (unique document)",
			document,
			0m);

		await groupRepository.AddAsync(singletonGroup, ct);
		return singletonGroup;
	}

	public async Task<StatisticsDto> GetStatisticsAsync(CancellationToken ct = default)
	{
		var groups = await groupRepository.GetAllAsync(ct);
		var totalDocs = groups.Sum(g => g.DocumentCount);

		return new StatisticsDto
		{
			TotalDocuments = totalDocs,
			TotalGroups = groups.Count,
			GroupsWithDuplicates = groups.Count(g => g.DocumentCount > 1),
			SingletonGroups = groups.Count(g => g.DocumentCount == 1),
			ConfidenceBreakdown = new Dictionary<string, int>
			{
				["very_high"] = groups.Count(g => g.Confidence == MatchConfidence.VeryHigh),
				["high"] = groups.Count(g => g.Confidence == MatchConfidence.High),
				["medium"] = groups.Count(g => g.Confidence == MatchConfidence.Medium),
				["none"] = groups.Count(g => g.Confidence == MatchConfidence.None),
			},
			DeduplicationRatio = totalDocs > 0 ? 1.0 - ((double)groups.Count / totalDocs) : 0
		};
	}

	private DocumentGroup CreateGroup(
		int groupNumber,
		List<Document> documents,
		MatchConfidence confidence,
		string matchReason,
		Document canonical,
		decimal similarityScore)
	{
		var group = new DocumentGroup
		{
			GroupNumber = groupNumber,
			Confidence = confidence,
			MatchReason = matchReason,
			CanonicalDocumentId = canonical.Id,
			DocumentCount = documents.Count
		};

		foreach (var doc in documents)
		{
			group.Memberships.Add(new DocumentGroupMembership
			{
				DocumentId = doc.Id,
				GroupId = group.Id,
				IsCanonical = doc.Id == canonical.Id,
				SimilarityScore = doc.Id == canonical.Id ? 1.0m : similarityScore
			});
		}

		return group;
	}

	private static void AddToGroup(DocumentGroup group, Document document, decimal similarityScore)
	{
		group.Memberships.Add(new DocumentGroupMembership
		{
			DocumentId = document.Id,
			GroupId = group.Id,
			IsCanonical = false,
			SimilarityScore = similarityScore
		});
		group.DocumentCount = group.Memberships.Count;
	}

	private int SelectCanonical(List<Document> documents)
	{
		var items = documents.Select(d => (d.OriginalText, GetMetadata(d))).ToList();
		return rulesEngine.SelectCanonicalIndex(items);
	}

	private static Dictionary<string, object?>? GetMetadata(Document doc)
	{
		var metadata = new Dictionary<string, object?>();
		if (doc.DocumentType != null) metadata["document_type"] = doc.DocumentType;
		if (doc.SourceFolder != null) metadata["source_folder"] = doc.SourceFolder;
		if (doc.DocumentDate.HasValue) metadata["document_date"] = doc.DocumentDate.Value.ToString("O");
		return metadata.Count > 0 ? metadata : null;
	}
}
