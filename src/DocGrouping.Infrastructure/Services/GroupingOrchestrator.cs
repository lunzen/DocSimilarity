using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using DocGrouping.Application.DTOs;
using DocGrouping.Application.Interfaces;
using DocGrouping.Domain.Projections;
using DocGrouping.Domain.Entities;
using DocGrouping.Domain.Enums;
using DocGrouping.Domain.Interfaces;
using DocGrouping.Infrastructure.Persistence;
using DocGrouping.Infrastructure.Rules;
using DocGrouping.Infrastructure.TextProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DocGrouping.Infrastructure.Services;

public class GroupingOrchestrator(
	IDocumentRepository documentRepository,
	IDocumentGroupRepository groupRepository,
	DocGroupingDbContext dbContext,
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

		const int groupBatchSize = 500;
		var groupBatch = new List<DocumentGroup>(groupBatchSize);
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
			groupBatch.Add(group);
			grouped.UnionWith(matchingDocs.Select(d => d.Id));

			if (groupBatch.Count >= groupBatchSize)
			{
				await groupRepository.AddRangeAsync(groupBatch, ct);
				dbContext.ChangeTracker.Clear();
				groupBatch.Clear();
			}
		}
		if (groupBatch.Count > 0)
		{
			await groupRepository.AddRangeAsync(groupBatch, ct);
			dbContext.ChangeTracker.Clear();
			groupBatch.Clear();
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
		groupBatch.Clear();
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
			groupBatch.Add(group);
			grouped.UnionWith(ungrouped.Select(d => d.Id));
			phase2Count++;

			if (groupBatch.Count >= groupBatchSize)
			{
				await groupRepository.AddRangeAsync(groupBatch, ct);
				dbContext.ChangeTracker.Clear();
				groupBatch.Clear();
				progress?.Report(new GroupingProgress("Phase 2",
					$"Saved {phase2Count} groups... {grouped.Count}/{documents.Count} docs grouped", 35));
			}
		}
		if (groupBatch.Count > 0)
		{
			await groupRepository.AddRangeAsync(groupBatch, ct);
			dbContext.ChangeTracker.Clear();
			groupBatch.Clear();
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
		groupBatch.Clear();
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
			groupBatch.Add(group);
			grouped.UnionWith(docsInGroup.Select(d => d.Id));
			phase3Count++;

			if (groupBatch.Count >= groupBatchSize)
			{
				await groupRepository.AddRangeAsync(groupBatch, ct);
				dbContext.ChangeTracker.Clear();
				groupBatch.Clear();
			}
		}
		if (groupBatch.Count > 0)
		{
			await groupRepository.AddRangeAsync(groupBatch, ct);
			dbContext.ChangeTracker.Clear();
			groupBatch.Clear();
		}

		metrics.Phase3Seconds = phaseSw.Elapsed.TotalSeconds;
		metrics.Phase3Groups = phase3Count;
		var pairRate = pairsCompared / Math.Max(metrics.Phase3Seconds, 0.001);
		logger.LogInformation(
			"Phase 3 (similarity): {Groups} groups | {Candidates:N0} candidates, {Verified:N0} verified, {Compared:N0} compared in {Time:F2}s ({Rate:N0} pairs/sec)",
			phase3Count, candidatePairCount, verifiedPairs.Count, pairsCompared, metrics.Phase3Seconds, pairRate);
		progress?.Report(new GroupingProgress("Phase 3",
			$"Done: {phase3Count} groups | {candidatePairCount:N0} candidates → {verifiedPairs.Count:N0} verified in {metrics.Phase3Seconds:F1}s", 78));

		// Persist MinHash signatures for all documents (seeds incremental runs)
		progress?.Report(new GroupingProgress("Phase 3", "Persisting MinHash signatures for incremental use...", 79));
		var allSignatures = new int[documents.Count][];
		Parallel.For(0, documents.Count, i =>
		{
			allSignatures[i] = fingerprinter.GenerateMinHashSignature(documents[i].NormalizedText);
		});
		await PersistMinHashSignaturesAsync(documents, allSignatures, clearExisting: true, ct);
		logger.LogInformation("Persisted {Count} MinHash signatures", documents.Count);

		// ── Phase 4: Singleton groups for unmatched documents ──
		phaseSw.Restart();
		var ungroupedForSingleton = documents.Where(d => !grouped.Contains(d.Id)).ToList();
		progress?.Report(new GroupingProgress("Phase 4",
			$"Creating singleton groups for {ungroupedForSingleton.Count} unmatched docs...", 80));

		var singletonCount = 0;
		const int singletonBatchSize = 500;
		var singletonBatch = new List<DocumentGroup>(singletonBatchSize);
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
			singletonBatch.Add(group);
			singletonCount++;

			if (singletonBatch.Count >= singletonBatchSize)
			{
				await groupRepository.AddRangeAsync(singletonBatch, ct);
				dbContext.ChangeTracker.Clear();
				singletonBatch.Clear();
				var pct = 80 + (int)(18.0 * singletonCount / ungroupedForSingleton.Count);
				progress?.Report(new GroupingProgress("Phase 4",
					$"Creating singletons... {singletonCount}/{ungroupedForSingleton.Count}", pct));
			}
		}
		if (singletonBatch.Count > 0)
		{
			await groupRepository.AddRangeAsync(singletonBatch, ct);
			dbContext.ChangeTracker.Clear();
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

	public async Task<List<CanonicalMatchResult>> ClassifyAgainstCanonicalsAsync(
		List<Guid>? targetDocumentIds = null,
		IProgress<GroupingProgress>? progress = null,
		CancellationToken ct = default)
	{
		var totalSw = Stopwatch.StartNew();
		logger.LogInformation("Starting canonical classification");
		progress?.Report(new GroupingProgress("Init", "Loading canonical references...", 2));

		// Load all canonicals and bucket by DocumentType
		var allCanonicals = await documentRepository.GetAllCanonicalsAsync(ct);
		if (allCanonicals.Count == 0)
		{
			logger.LogWarning("No canonical reference documents found");
			progress?.Report(new GroupingProgress("Done", "No canonical references found. Mark documents as canonical first.", 100));
			return [];
		}

		var canonicalsByType = allCanonicals
			.Where(c => !string.IsNullOrEmpty(c.DocumentType))
			.GroupBy(c => c.DocumentType!)
			.ToDictionary(g => g.Key, g => g.ToList());

		logger.LogInformation("Loaded {Count} canonicals across {Types} document types",
			allCanonicals.Count, canonicalsByType.Count);
		progress?.Report(new GroupingProgress("Init",
			$"Loaded {allCanonicals.Count} canonicals across {canonicalsByType.Count} types", 5));

		// Clear existing groups before reclassifying
		await groupRepository.DeleteAllAsync(ct);

		// Load target documents
		List<Document> targetDocs;
		if (targetDocumentIds is { Count: > 0 })
		{
			targetDocs = [];
			foreach (var id in targetDocumentIds)
			{
				var doc = await documentRepository.GetByIdAsync(id, ct);
				if (doc != null && !doc.IsCanonicalReference)
					targetDocs.Add(doc);
			}
		}
		else
		{
			var allDocs = await documentRepository.GetAllAsync(ct);
			targetDocs = allDocs.Where(d => !d.IsCanonicalReference).ToList();
		}

		logger.LogInformation("Classifying {Count} target documents against canonicals", targetDocs.Count);
		progress?.Report(new GroupingProgress("Classify",
			$"Classifying {targetDocs.Count} documents...", 10));

		var results = new List<CanonicalMatchResult>();
		var nextGroupNumber = 1;

		// First, create groups for each canonical (so matched docs join canonical's group)
		var canonicalGroups = new Dictionary<Guid, DocumentGroup>();
		foreach (var canonical in allCanonicals)
		{
			var group = CreateGroup(
				nextGroupNumber++,
				[canonical],
				MatchConfidence.VeryHigh,
				"Canonical reference document",
				canonical,
				1.0m);
			canonicalGroups[canonical.Id] = group;
			await groupRepository.AddAsync(group, ct);
		}

		for (int i = 0; i < targetDocs.Count; i++)
		{
			var doc = targetDocs[i];

			// Find the right bucket of canonicals to compare against
			List<Document> candidates;
			if (!string.IsNullOrEmpty(doc.DocumentType) && canonicalsByType.TryGetValue(doc.DocumentType, out var typedCandidates))
			{
				candidates = typedCandidates;
			}
			else
			{
				// Fallback: compare against all canonicals
				candidates = allCanonicals;
			}

			var matchResult = FindBestCanonicalMatch(doc, candidates);

			if (matchResult.HasValue)
			{
				var (matchedCanonical, confidence, similarity, reason) = matchResult.Value;

				// Add to the canonical's group
				var group = canonicalGroups[matchedCanonical.Id];
				AddToGroup(group, doc, similarity);
				await groupRepository.UpdateAsync(group, ct);

				results.Add(new CanonicalMatchResult(
					doc.Id, doc.FileName, doc.DocumentType,
					matchedCanonical.Id, matchedCanonical.FileName,
					confidence, similarity, reason));
			}
			else
			{
				// No match → singleton group
				var singletonGroup = CreateGroup(
					nextGroupNumber++,
					[doc],
					MatchConfidence.None,
					"No canonical match found",
					doc,
					0m);
				await groupRepository.AddAsync(singletonGroup, ct);

				results.Add(new CanonicalMatchResult(
					doc.Id, doc.FileName, doc.DocumentType,
					null, null,
					MatchConfidence.None, 0m, "No canonical match found"));
			}

			if ((i + 1) % 10 == 0 || i == targetDocs.Count - 1)
			{
				var pct = 10 + (int)(85.0 * (i + 1) / targetDocs.Count);
				progress?.Report(new GroupingProgress("Classify",
					$"Classified {i + 1}/{targetDocs.Count} documents...", pct));
			}
		}

		totalSw.Stop();
		var matched = results.Count(r => r.MatchedCanonicalId.HasValue);
		logger.LogInformation(
			"Canonical classification complete: {Matched}/{Total} matched in {Time:F2}s",
			matched, results.Count, totalSw.Elapsed.TotalSeconds);

		progress?.Report(new GroupingProgress("Done",
			$"Complete: {matched}/{results.Count} documents matched to canonicals in {totalSw.Elapsed.TotalSeconds:F1}s", 100));

		return results;
	}

	private (Document Canonical, MatchConfidence Confidence, decimal Similarity, string Reason)? FindBestCanonicalMatch(
		Document doc, List<Document> candidates)
	{
		// Phase 1: Exact text hash match
		var textHashMatch = candidates.FirstOrDefault(c => c.TextHash == doc.TextHash);
		if (textHashMatch != null)
		{
			return (textHashMatch, MatchConfidence.VeryHigh, 1.0m, "Exact text match with canonical");
		}

		// Phase 2: Fuzzy hash match
		var fuzzyHashMatch = candidates.FirstOrDefault(c => c.FuzzyHash == doc.FuzzyHash);
		if (fuzzyHashMatch != null)
		{
			return (fuzzyHashMatch, MatchConfidence.High, 0.9m, "Fuzzy hash match with canonical");
		}

		// Phase 3: Best Jaccard >= 0.70 (no upper bound, unlike batch mode)
		Document? bestCandidate = null;
		decimal bestSimilarity = 0m;

		foreach (var candidate in candidates)
		{
			var metrics = fingerprinter.CalculateSimilarityMetrics(
				doc.NormalizedText, candidate.NormalizedText);

			if (metrics.JaccardSimilarity >= 0.70 && (decimal)metrics.JaccardSimilarity > bestSimilarity)
			{
				bestSimilarity = (decimal)metrics.JaccardSimilarity;
				bestCandidate = candidate;
			}
		}

		if (bestCandidate != null)
		{
			var confidence = bestSimilarity >= 0.85m ? MatchConfidence.High : MatchConfidence.Medium;
			return (bestCandidate, confidence, bestSimilarity,
				$"Jaccard similarity {bestSimilarity:P1} with canonical");
		}

		return null;
	}

	public async Task<StatisticsDto> GetStatisticsAsync(CancellationToken ct = default)
	{
		// Lightweight aggregate query — no entity loading
		var summaries = await dbContext.DocumentGroups
			.AsNoTracking()
			.GroupBy(g => g.Confidence)
			.Select(grp => new
			{
				Confidence = grp.Key,
				Count = grp.Count(),
				TotalDocs = grp.Sum(g => g.DocumentCount),
				Duplicates = grp.Count(g => g.DocumentCount > 1),
				Singletons = grp.Count(g => g.DocumentCount == 1)
			})
			.ToListAsync(ct);

		var totalDocs = summaries.Sum(s => s.TotalDocs);
		var totalGroups = summaries.Sum(s => s.Count);

		return new StatisticsDto
		{
			TotalDocuments = totalDocs,
			TotalGroups = totalGroups,
			GroupsWithDuplicates = summaries.Sum(s => s.Duplicates),
			SingletonGroups = summaries.Sum(s => s.Singletons),
			ConfidenceBreakdown = new Dictionary<string, int>
			{
				["very_high"] = summaries.FirstOrDefault(s => s.Confidence == MatchConfidence.VeryHigh)?.Count ?? 0,
				["high"] = summaries.FirstOrDefault(s => s.Confidence == MatchConfidence.High)?.Count ?? 0,
				["medium"] = summaries.FirstOrDefault(s => s.Confidence == MatchConfidence.Medium)?.Count ?? 0,
				["none"] = summaries.FirstOrDefault(s => s.Confidence == MatchConfidence.None)?.Count ?? 0,
			},
			DeduplicationRatio = totalDocs > 0 ? 1.0 - ((double)totalGroups / totalDocs) : 0
		};
	}

	public async Task<List<DocumentGroup>> GroupIncrementalAsync(
		IProgress<GroupingProgress>? progress = null,
		CancellationToken ct = default)
	{
		// Start with a clean change tracker to avoid stale entity conflicts
		dbContext.ChangeTracker.Clear();

		var totalSw = Stopwatch.StartNew();
		var phaseSw = new Stopwatch();
		var metrics = new GroupingMetrics();

		const int queryChunkSize = 1000;
		const int saveBatchSize = 500;

		logger.LogInformation("Starting incremental grouping of ungrouped documents");
		progress?.Report(new GroupingProgress("Init", "Loading ungrouped documents...", 2));

		// Load ungrouped docs as lightweight projections (drops OriginalText)
		var newDocs = await documentRepository.GetUngroupedHashesAsync(ct);
		if (newDocs.Count == 0)
		{
			logger.LogInformation("No ungrouped documents found — nothing to do");
			progress?.Report(new GroupingProgress("Done", "No ungrouped documents to process.", 100));
			return [];
		}

		metrics.NewDocumentsCount = newDocs.Count;

		// Check if any groups exist at all — if not, delegate to full regroup (first run)
		var existingGroupCount = await groupRepository.GetCountAsync(ct: ct);
		metrics.ExistingGroupsCount = existingGroupCount;

		if (existingGroupCount == 0)
		{
			logger.LogInformation("No existing groups found — delegating to full regroup (first run)");
			progress?.Report(new GroupingProgress("Init", "No existing groups — running full regroup for initial indexing...", 5));
			return await GroupAllDocumentsAsync(progress, ct);
		}

		logger.LogInformation("Incremental grouping: {NewDocs} new docs against {ExistingGroups} existing groups",
			newDocs.Count, existingGroupCount);
		progress?.Report(new GroupingProgress("Init",
			$"Found {newDocs.Count} ungrouped docs, {existingGroupCount} existing groups", 5));

		var grouped = new HashSet<Guid>();
		var newGroups = new List<DocumentGroup>();
		var nextGroupNumber = await groupRepository.GetNextGroupNumberAsync(ct);
		int joinedExisting = 0;

		// ── Phase 1: Exact Hash — chunked lookup, batched saves ──
		phaseSw.Restart();
		progress?.Report(new GroupingProgress("Phase 1", "Matching exact text hashes...", 8));

		var newDocTextHashes = newDocs.Select(d => d.TextHash).Distinct().ToList();

		// Chunked hash→group lookup (no full entities loaded)
		var textHashToGroupId = new Dictionary<string, Guid>();
		foreach (var chunk in newDocTextHashes.Chunk(queryChunkSize))
		{
			var lookups = await documentRepository.GetGroupedByTextHashesAsync(chunk, ct);
			foreach (var lookup in lookups)
				textHashToGroupId.TryAdd(lookup.Hash, lookup.GroupId);
		}

		// Group new docs by text hash for batch processing
		var newByTextHash = newDocs.GroupBy(d => d.TextHash).ToDictionary(g => g.Key, g => g.ToList());
		var groupCache = new Dictionary<Guid, DocumentGroup>();
		var pendingUpdateCount = 0;

		foreach (var (textHash, docsWithHash) in newByTextHash)
		{
			if (!textHashToGroupId.TryGetValue(textHash, out var groupId)) continue;

			if (!groupCache.TryGetValue(groupId, out var existingGroup))
			{
				existingGroup = await groupRepository.GetByIdAsync(groupId, ct);
				if (existingGroup == null) continue;
				groupCache[groupId] = existingGroup;
			}

			foreach (var newDoc in docsWithHash.Where(d => !grouped.Contains(d.Id)))
			{
				AddToGroup(existingGroup, newDoc.Id, 1.0m);
				grouped.Add(newDoc.Id);
				joinedExisting++;
				pendingUpdateCount++;
			}

			if (pendingUpdateCount >= saveBatchSize)
			{
				await FlushPendingChangesAsync(ct);
				groupCache.Clear();
				pendingUpdateCount = 0;
			}
		}
		if (pendingUpdateCount > 0)
		{
			await FlushPendingChangesAsync(ct);
			groupCache.Clear();
			pendingUpdateCount = 0;
		}

		metrics.Phase1Seconds = phaseSw.Elapsed.TotalSeconds;
		metrics.Phase1Groups = newGroups.Count;
		logger.LogInformation("Phase 1 (exact hash): {Joined} joined existing in {Time:F2}s",
			joinedExisting, metrics.Phase1Seconds);
		progress?.Report(new GroupingProgress("Phase 1",
			$"Done: {joinedExisting} joined existing groups in {metrics.Phase1Seconds:F1}s", 25));

		// ── Phase 2: Fuzzy Hash — same chunked pattern ──
		phaseSw.Restart();
		var remainingNewDocs = newDocs.Where(d => !grouped.Contains(d.Id)).ToList();
		if (remainingNewDocs.Count == 0) goto PhaseComplete;

		progress?.Report(new GroupingProgress("Phase 2",
			$"Matching fuzzy hashes for {remainingNewDocs.Count} remaining docs...", 30));

		var newDocFuzzyHashes = remainingNewDocs.Select(d => d.FuzzyHash).Distinct().ToList();

		var fuzzyHashToGroupId = new Dictionary<string, Guid>();
		foreach (var chunk in newDocFuzzyHashes.Chunk(queryChunkSize))
		{
			var lookups = await documentRepository.GetGroupedByFuzzyHashesAsync(chunk, ct);
			foreach (var lookup in lookups)
				fuzzyHashToGroupId.TryAdd(lookup.Hash, lookup.GroupId);
		}

		var newByFuzzyHash = remainingNewDocs.GroupBy(d => d.FuzzyHash).ToDictionary(g => g.Key, g => g.ToList());
		var phase2Joined = 0;

		foreach (var (fuzzyHash, docsWithHash) in newByFuzzyHash)
		{
			if (!fuzzyHashToGroupId.TryGetValue(fuzzyHash, out var groupId)) continue;

			if (!groupCache.TryGetValue(groupId, out var existingGroup))
			{
				existingGroup = await groupRepository.GetByIdAsync(groupId, ct);
				if (existingGroup == null) continue;
				groupCache[groupId] = existingGroup;
			}

			foreach (var newDoc in docsWithHash.Where(d => !grouped.Contains(d.Id)))
			{
				AddToGroup(existingGroup, newDoc.Id, 0.9m);
				grouped.Add(newDoc.Id);
				phase2Joined++;
				joinedExisting++;
				pendingUpdateCount++;
			}

			if (pendingUpdateCount >= saveBatchSize)
			{
				await FlushPendingChangesAsync(ct);
				groupCache.Clear();
				pendingUpdateCount = 0;
			}
		}
		if (pendingUpdateCount > 0)
		{
			await FlushPendingChangesAsync(ct);
			groupCache.Clear();
			pendingUpdateCount = 0;
		}

		metrics.Phase2Seconds = phaseSw.Elapsed.TotalSeconds;
		metrics.Phase2Groups = 0;
		logger.LogInformation("Phase 2 (fuzzy hash): {Joined} joined existing in {Time:F2}s",
			phase2Joined, metrics.Phase2Seconds);
		progress?.Report(new GroupingProgress("Phase 2",
			$"Done: {phase2Joined} joined existing in {metrics.Phase2Seconds:F1}s", 45));

		// ── Phase 3: LSH + MinHash — projection-based, MinHash pre-filter ──
		phaseSw.Restart();
		remainingNewDocs = newDocs.Where(d => !grouped.Contains(d.Id)).ToList();
		metrics.Phase3UngroupedCount = remainingNewDocs.Count;

		if (remainingNewDocs.Count == 0) goto PhaseComplete;

		progress?.Report(new GroupingProgress("Phase 3",
			$"Loading persisted MinHash signatures for LSH comparison ({remainingNewDocs.Count} remaining docs)...", 48));

		// Load existing signatures as projection: (Signature, DocumentId) — no Document entity
		const int sigChunkSize = 10_000;
		var existingSigData = new List<int[]>();
		var existingSigDocIds = new List<Guid>();

		var totalSigCount = await dbContext.MinHashSignatures
			.Where(s => s.Document.GroupMembership != null)
			.CountAsync(ct);

		var sigOffset = 0;
		while (sigOffset < totalSigCount)
		{
			var chunk = await dbContext.MinHashSignatures
				.AsNoTracking()
				.Where(s => s.Document.GroupMembership != null)
				.OrderBy(s => s.Id)
				.Skip(sigOffset)
				.Take(sigChunkSize)
				.Select(s => new { s.Signature, s.DocumentId })
				.ToListAsync(ct);

			foreach (var item in chunk)
			{
				existingSigData.Add(item.Signature);
				existingSigDocIds.Add(item.DocumentId);
			}

			sigOffset += chunk.Count;
			if (chunk.Count < sigChunkSize) break;
		}
		dbContext.ChangeTracker.Clear();

		logger.LogInformation("Phase 3: Loaded {Count} existing MinHash signatures as projections", existingSigData.Count);

		// Batch-load DocumentId→GroupId mapping for all existing sig docs
		var docIdToGroupId = new Dictionary<Guid, Guid>();
		foreach (var chunk in existingSigDocIds.Distinct().Chunk(queryChunkSize))
		{
			var memberships = await dbContext.DocumentGroupMemberships
				.AsNoTracking()
				.Where(m => chunk.Contains(m.DocumentId))
				.Select(m => new { m.DocumentId, m.GroupId })
				.ToListAsync(ct);
			foreach (var m in memberships)
				docIdToGroupId.TryAdd(m.DocumentId, m.GroupId);
		}

		// Compute signatures for new remaining docs
		progress?.Report(new GroupingProgress("Phase 3",
			$"Computing MinHash signatures for {remainingNewDocs.Count} new docs...", 50));

		var newSignatures = new int[remainingNewDocs.Count][];
		Parallel.For(0, remainingNewDocs.Count, i =>
		{
			newSignatures[i] = fingerprinter.GenerateMinHashSignature(remainingNewDocs[i].NormalizedText);
		});

		// Build LSH index from existing signatures only (new docs query against it)
		progress?.Report(new GroupingProgress("Phase 3", "Building LSH index from existing signatures...", 55));
		var lshIndex = new MinHashLshIndex(bands: 20, rowsPerBand: 5);
		for (var i = 0; i < existingSigData.Count; i++)
			lshIndex.Add(i, existingSigData[i]);

		// For each new doc, query candidates among existing docs
		var phase3Joined = 0;
		long candidateCount = 0;
		long verifiedCount = 0;
		long prefilterSkipped = 0;
		const double minHashPrefilterThreshold = 0.60;

		progress?.Report(new GroupingProgress("Phase 3",
			$"Querying {remainingNewDocs.Count} new docs against {existingSigData.Count} existing signatures...", 60));

		for (var i = 0; i < remainingNewDocs.Count; i++)
		{
			var newDoc = remainingNewDocs[i];
			if (grouped.Contains(newDoc.Id)) continue;

			var candidates = lshIndex.QueryCandidates(newSignatures[i]);
			candidateCount += candidates.Count;

			Guid? bestGroupId = null;
			decimal bestSimilarity = 0m;

			foreach (var existIdx in candidates)
			{
				// MinHash pre-filter: estimate Jaccard before loading text
				var estimatedJaccard = fingerprinter.EstimateJaccardFromMinHash(
					newSignatures[i], existingSigData[existIdx]);

				if (estimatedJaccard < minHashPrefilterThreshold)
				{
					prefilterSkipped++;
					continue;
				}

				verifiedCount++;

				// Load NormalizedText on-demand for the existing doc
				var existDocId = existingSigDocIds[existIdx];
				var existNormText = await dbContext.Documents
					.AsNoTracking()
					.Where(d => d.Id == existDocId)
					.Select(d => d.NormalizedText)
					.FirstOrDefaultAsync(ct);

				if (existNormText == null) continue;

				var sim = fingerprinter.CalculateSimilarityMetrics(
					newDoc.NormalizedText, existNormText);

				if (sim.JaccardSimilarity >= 0.70 && (decimal)sim.JaccardSimilarity > bestSimilarity)
				{
					bestSimilarity = (decimal)sim.JaccardSimilarity;
					if (docIdToGroupId.TryGetValue(existDocId, out var gId))
						bestGroupId = gId;
				}
			}

			if (bestGroupId != null)
			{
				if (!groupCache.TryGetValue(bestGroupId.Value, out var existingGroup))
				{
					existingGroup = await groupRepository.GetByIdAsync(bestGroupId.Value, ct);
					if (existingGroup != null)
						groupCache[bestGroupId.Value] = existingGroup;
				}

				if (existingGroup != null)
				{
					var confidence = bestSimilarity >= 0.85m ? MatchConfidence.High : MatchConfidence.Medium;
					AddToGroup(existingGroup, newDoc.Id, bestSimilarity);
					if (existingGroup.Confidence < confidence)
					{
						existingGroup.Confidence = confidence;
						existingGroup.MatchReason = $"Content similarity ({bestSimilarity:P1} Jaccard, incremental)";
					}
					grouped.Add(newDoc.Id);
					phase3Joined++;
					joinedExisting++;
					pendingUpdateCount++;
				}
			}

			if (pendingUpdateCount >= saveBatchSize)
			{
				await FlushPendingChangesAsync(ct);
				groupCache.Clear();
				pendingUpdateCount = 0;
			}

			if ((i + 1) % 500 == 0)
			{
				var pct = 60 + (int)(15.0 * (i + 1) / remainingNewDocs.Count);
				progress?.Report(new GroupingProgress("Phase 3",
					$"Processed {i + 1}/{remainingNewDocs.Count} docs ({phase3Joined} joined, {prefilterSkipped:N0} pre-filtered)...", pct));
			}
		}
		if (pendingUpdateCount > 0)
		{
			await FlushPendingChangesAsync(ct);
			groupCache.Clear();
			pendingUpdateCount = 0;
		}

		// Persist new MinHash signatures with batched existence check
		await PersistMinHashSignaturesAsync(remainingNewDocs, newSignatures, clearExisting: false, ct);

		metrics.Phase3Seconds = phaseSw.Elapsed.TotalSeconds;
		metrics.Phase3Groups = 0;
		metrics.Phase3CandidatePairs = candidateCount;
		metrics.Phase3VerifiedPairs = verifiedCount;
		metrics.Phase3PairsCompared = verifiedCount;
		logger.LogInformation(
			"Phase 3 (LSH): {Joined} joined existing, {Candidates:N0} candidates, {Verified:N0} verified, {Skipped:N0} pre-filtered in {Time:F2}s",
			phase3Joined, candidateCount, verifiedCount, prefilterSkipped, metrics.Phase3Seconds);
		progress?.Report(new GroupingProgress("Phase 3",
			$"Done: {phase3Joined} joined, {verifiedCount:N0} verified, {prefilterSkipped:N0} pre-filtered in {metrics.Phase3Seconds:F1}s", 78));

	PhaseComplete:

		// ── Phase 4: Singletons for remaining new docs ──
		phaseSw.Restart();
		var singletonDocIds = newDocs.Where(d => !grouped.Contains(d.Id)).Select(d => d.Id).ToList();
		progress?.Report(new GroupingProgress("Phase 4",
			$"Creating singleton groups for {singletonDocIds.Count} unmatched docs...", 80));

		var singletonCount = 0;
		const int singletonBatchSize = 500;
		var singletonBatch = new List<DocumentGroup>(singletonBatchSize);
		foreach (var docId in singletonDocIds)
		{
			var group = CreateGroupFromId(nextGroupNumber++, docId,
				MatchConfidence.None, "No matches found (unique document, incremental)", 0m);
			newGroups.Add(group);
			singletonBatch.Add(group);
			singletonCount++;

			if (singletonBatch.Count >= singletonBatchSize)
			{
				await groupRepository.AddRangeAsync(singletonBatch, ct);
				dbContext.ChangeTracker.Clear();
				singletonBatch.Clear();
				var pct = 80 + (int)(18.0 * singletonCount / singletonDocIds.Count);
				progress?.Report(new GroupingProgress("Phase 4",
					$"Creating singletons... {singletonCount}/{singletonDocIds.Count}", pct));
			}
		}
		if (singletonBatch.Count > 0)
		{
			await groupRepository.AddRangeAsync(singletonBatch, ct);
			dbContext.ChangeTracker.Clear();
		}

		metrics.Phase4Seconds = phaseSw.Elapsed.TotalSeconds;
		metrics.Phase4Singletons = singletonCount;
		metrics.TotalSeconds = totalSw.Elapsed.TotalSeconds;
		metrics.TotalGroups = existingGroupCount + newGroups.Count;
		metrics.TotalDocuments = newDocs.Count;
		metrics.JoinedExistingGroups = joinedExisting;
		metrics.NewGroupsFormed = newGroups.Count;

		logger.LogInformation(
			"Incremental grouping complete: {NewDocs} new docs → {Joined} joined existing, {NewGroups} new groups, {Singletons} singletons in {Time:F2}s",
			newDocs.Count, joinedExisting, newGroups.Count - singletonCount, singletonCount, metrics.TotalSeconds);

		progress?.Report(new GroupingProgress("Done",
			$"Complete: {newDocs.Count} new docs processed — {joinedExisting} joined existing, {newGroups.Count - singletonCount} new groups, {singletonCount} singletons in {metrics.TotalSeconds:F1}s",
			100, metrics));

		return newGroups;
	}

	private async Task PersistMinHashSignaturesAsync(
		List<Document> docs, int[][] signatures, bool clearExisting, CancellationToken ct)
	{
		var docIds = docs.Select(d => d.Id).ToList();
		await PersistMinHashSignaturesCoreAsync(docIds, signatures, clearExisting, ct);
	}

	private async Task PersistMinHashSignaturesAsync(
		List<DocumentHashProjection> docs, int[][] signatures, bool clearExisting, CancellationToken ct)
	{
		var docIds = docs.Select(d => d.Id).ToList();
		await PersistMinHashSignaturesCoreAsync(docIds, signatures, clearExisting, ct);
	}

	private async Task PersistMinHashSignaturesCoreAsync(
		List<Guid> docIds, int[][] signatures, bool clearExisting, CancellationToken ct)
	{
		if (clearExisting)
		{
			await dbContext.MinHashSignatures.ExecuteDeleteAsync(ct);
		}

		// Batch existence check: load all IDs that already have signatures
		var existingDocIds = new HashSet<Guid>();
		if (!clearExisting)
		{
			const int existCheckChunk = 1000;
			foreach (var chunk in docIds.Chunk(existCheckChunk))
			{
				var existing = await dbContext.MinHashSignatures
					.AsNoTracking()
					.Where(s => chunk.Contains(s.DocumentId))
					.Select(s => s.DocumentId)
					.ToListAsync(ct);
				existingDocIds.UnionWith(existing);
			}
		}

		var batch = new List<MinHashSignature>();
		for (var i = 0; i < docIds.Count; i++)
		{
			if (!clearExisting && existingDocIds.Contains(docIds[i]))
				continue;

			batch.Add(new MinHashSignature
			{
				DocumentId = docIds[i],
				Signature = signatures[i]
			});

			if (batch.Count >= 500)
			{
				dbContext.MinHashSignatures.AddRange(batch);
				await dbContext.SaveChangesAsync(ct);
				dbContext.ChangeTracker.Clear();
				batch.Clear();
			}
		}

		if (batch.Count > 0)
		{
			dbContext.MinHashSignatures.AddRange(batch);
			await dbContext.SaveChangesAsync(ct);
			dbContext.ChangeTracker.Clear();
		}
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

	private void AddToGroup(DocumentGroup group, Document document, decimal similarityScore)
	{
		AddToGroup(group, document.Id, similarityScore);
	}

	private void AddToGroup(DocumentGroup group, Guid documentId, decimal similarityScore)
	{
		var membership = new DocumentGroupMembership
		{
			DocumentId = documentId,
			GroupId = group.Id,
			IsCanonical = false,
			SimilarityScore = similarityScore
		};
		group.Memberships.Add(membership);
		group.DocumentCount = group.Memberships.Count;
		// Explicitly track the new entity — EF may not auto-detect it
		// when the membership is added to a tracked collection by ID only.
		dbContext.DocumentGroupMemberships.Add(membership);
	}

	private static DocumentGroup CreateGroupFromId(
		int groupNumber,
		Guid documentId,
		MatchConfidence confidence,
		string matchReason,
		decimal similarityScore)
	{
		var group = new DocumentGroup
		{
			GroupNumber = groupNumber,
			Confidence = confidence,
			MatchReason = matchReason,
			CanonicalDocumentId = documentId,
			DocumentCount = 1
		};

		group.Memberships.Add(new DocumentGroupMembership
		{
			DocumentId = documentId,
			GroupId = group.Id,
			IsCanonical = true,
			SimilarityScore = similarityScore == 0m ? 1.0m : similarityScore
		});

		return group;
	}

	/// <summary>
	/// Fix phantom-Modified memberships, save changes, then clear the tracker.
	/// Reused across all phases of incremental grouping.
	/// </summary>
	private async Task FlushPendingChangesAsync(CancellationToken ct)
	{
		// Fix phantom-Modified memberships (same fix as DocumentGroupRepository.UpdateAsync)
		foreach (var entry in dbContext.ChangeTracker.Entries<DocumentGroupMembership>()
			.Where(e => e.State == EntityState.Modified))
		{
			var hasRealChange = entry.Properties.Any(p => p.IsModified &&
				!Equals(p.OriginalValue, p.CurrentValue));
			if (!hasRealChange)
				entry.State = EntityState.Unchanged;
		}

		await dbContext.SaveChangesAsync(ct);
		dbContext.ChangeTracker.Clear();
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
